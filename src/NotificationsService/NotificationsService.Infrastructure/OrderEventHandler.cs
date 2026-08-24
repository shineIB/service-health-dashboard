using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotificationsService.Domain;
using NotificationsService.Infrastructure.Telemetry;

namespace NotificationsService.Infrastructure;

// Broken out from OrderEventConsumer so the message-handling logic (deserialize, map, send)
// is directly testable without a running RabbitMQ connection — same reasoning as
// dashboard-service's ServiceHealthChecker being split out from its BackgroundService.
public sealed class OrderEventHandler
{
    private readonly INotificationSender _sender;
    private readonly ILogger<OrderEventHandler> _logger;

    public OrderEventHandler(INotificationSender sender, ILogger<OrderEventHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    // Returns true to ack, false to nack (routed to the dead-letter queue by
    // OrderEventConsumer's queue arguments — see RabbitMqOptions.DeadLetterExchangeName).
    public async Task<bool> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        using var activity = NotificationsTelemetry.ActivitySource.StartActivity("notifications.handle-event");

        OrderEventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<OrderEventEnvelope>(body.Span);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not deserialize order event payload; dead-lettering.");
            NotificationsTelemetry.NotificationsFailed.Add(1, new KeyValuePair<string, object?>("reason", "deserialize_error"));
            return false;
        }

        if (envelope is null || !TryMapEventType(envelope.EventType, out var eventType))
        {
            _logger.LogWarning("Unknown or missing event type {EventType}; dead-lettering.", envelope?.EventType);
            NotificationsTelemetry.NotificationsFailed.Add(1, new KeyValuePair<string, object?>("reason", "unknown_event_type"));
            return false;
        }

        activity?.SetTag("order.id", envelope.OrderId);
        activity?.SetTag("messaging.event_type", envelope.EventType);

        var notification = new OrderNotification(envelope.OrderId, envelope.CustomerId, eventType, envelope.OccurredAtUtc);

        try
        {
            await _sender.SendAsync(notification, cancellationToken);
            NotificationsTelemetry.NotificationsSent.Add(1, new KeyValuePair<string, object?>("event_type", envelope.EventType));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to send notification for order {OrderId}; dead-lettering.", envelope.OrderId);
            NotificationsTelemetry.NotificationsFailed.Add(1, new KeyValuePair<string, object?>("reason", "send_failed"));
            return false;
        }
    }

    private static bool TryMapEventType(string? raw, out OrderEventType eventType)
    {
        switch (raw)
        {
            case "order.created":
                eventType = OrderEventType.Created;
                return true;
            case "order.confirmed":
                eventType = OrderEventType.Confirmed;
                return true;
            case "order.cancelled":
                eventType = OrderEventType.Cancelled;
                return true;
            default:
                eventType = default;
                return false;
        }
    }
}
