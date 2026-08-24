using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrdersService.Domain;
using OrdersService.Infrastructure.Telemetry;
using RabbitMQ.Client;

namespace OrdersService.Infrastructure;

// Wire shape this service publishes. notifications-service keeps its own, separately
// deserialized copy of this shape (OrderEventEnvelope) rather than referencing this type —
// same "local shape per service" principle as InventoryClient's ProblemDetailsPayload, so
// the two services stay independently deployable.
internal sealed record OrderEventPayload(
    Guid EventId,
    string EventType,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAtUtc);

public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    public RabbitMqEventPublisher(
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqOptions options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options;
        _logger = logger;
    }

    public Task PublishOrderCreatedAsync(Order order, CancellationToken cancellationToken) =>
        PublishAsync("order.created", order, cancellationToken);

    public Task PublishOrderConfirmedAsync(Order order, CancellationToken cancellationToken) =>
        PublishAsync("order.confirmed", order, cancellationToken);

    public Task PublishOrderCancelledAsync(Order order, CancellationToken cancellationToken) =>
        PublishAsync("order.cancelled", order, cancellationToken);

    private async Task PublishAsync(string routingKey, Order order, CancellationToken cancellationToken)
    {
        using var activity = MessagingTelemetry.ActivitySource.StartActivity("order.publish-event");
        activity?.SetTag("messaging.routing_key", routingKey);
        activity?.SetTag("order.id", order.Id);

        try
        {
            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: false);
            await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);

            // Declared idempotently on every publish (matches the exchange declared by
            // notifications-service's consumer) rather than once at startup — publishing is
            // already lazy-connect, so there's no separate "topology setup" step to hook into.
            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var payload = new OrderEventPayload(
                Guid.NewGuid(),
                routingKey,
                order.Id,
                order.CustomerId,
                DateTimeOffset.UtcNow);

            var body = JsonSerializer.SerializeToUtf8Bytes(payload);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
            };

            // With publisher confirmations enabled on the channel, this awaits the broker's
            // ack and throws PublishException on a nack/return — that's what turns a lost
            // message into something this catch block actually observes, instead of a
            // publish that looks successful but never arrived.
            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            MessagingTelemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("event_type", routingKey));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: the order is already committed by the time this is called (see
            // OrderEndpoints.cs). A lost event here means a missed notification, not a lost
            // order — logged and counted, never rethrown.
            activity?.SetTag("error.type", ex.GetType().FullName);
            MessagingTelemetry.EventsPublishFailed.Add(1, new KeyValuePair<string, object?>("event_type", routingKey));
            _logger.LogWarning(ex, "Failed to publish {RoutingKey} event for order {OrderId}.", routingKey, order.Id);
        }
    }
}
