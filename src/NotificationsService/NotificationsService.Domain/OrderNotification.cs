namespace NotificationsService.Domain;

public sealed record OrderNotification(
    Guid OrderId,
    Guid CustomerId,
    OrderEventType EventType,
    DateTimeOffset OccurredAtUtc);
