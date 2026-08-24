namespace NotificationsService.Infrastructure;

// Local shape of the JSON orders-service publishes (its OrderEventPayload) — deliberately not
// a shared type between the two services, same "local shape per service" principle as
// InventoryClient's ProblemDetailsPayload in orders-service.
internal sealed record OrderEventEnvelope(
    Guid EventId,
    string EventType,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAtUtc);
