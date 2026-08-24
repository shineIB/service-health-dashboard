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

    // Set once Attempts reaches OutboxOptions.MaxAttempts — see OutboxDispatcher. Excluded from
    // the dispatcher's pending-message query the same way PublishedAtUtc is, so a message that
    // can never be published stops consuming a batch slot forever instead of crowding out
    // messages behind it. The row and its full PayloadJson/LastError stay in the table for
    // inspection; setting this back to null (e.g. via a manual UPDATE) re-queues it for another
    // round of attempts.
    public DateTimeOffset? FailedAtUtc { get; set; }
}
