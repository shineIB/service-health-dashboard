using RabbitMQ.Client;

namespace OrdersService.Infrastructure;

// Raw publish to RabbitMQ — no outbox awareness, no catch-and-swallow. Used only by
// OutboxDispatcher, which already knows how to retry (leave the row unpublished, try again
// next poll) and needs the exception to actually observe a failure, unlike the old
// best-effort-from-the-request-path design this replaced (see CLAUDE.md, step 7.5).
public sealed class RabbitMqOutboxSender
{
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;

    public RabbitMqOutboxSender(RabbitMqConnectionProvider connectionProvider, RabbitMqOptions options)
    {
        _connectionProvider = connectionProvider;
        _options = options;
    }

    public async Task PublishAsync(string routingKey, byte[] payload, CancellationToken cancellationToken)
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

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };

        // With publisher confirmations enabled on the channel, this awaits the broker's ack
        // and throws PublishException on a nack/return — that's what turns a lost message into
        // something OutboxDispatcher's catch block actually observes and retries.
        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: payload,
            cancellationToken: cancellationToken);
    }
}
