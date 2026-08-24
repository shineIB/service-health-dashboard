namespace OrdersService.Infrastructure;

// EF entity only — never exposed through IOrderEventOutbox (Domain doesn't need to know the
// outbox is a table). Id doubles as the event's own stable identity: it's generated once when
// the row is staged and reused unchanged in every publish attempt (including retries after a
// crash), which is what lets notifications-service dedupe by EventId — see
// NotificationsService.Domain.IProcessedEventStore.
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }
    public required Guid OrderId { get; init; }
    public required string EventType { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
