namespace NotificationsService.Domain;

// RabbitMQ guarantees at-least-once delivery, not exactly-once — a redelivered message
// (e.g. the dispatcher retried an outbox row that had actually already been published, or a
// connection dropped after we processed a message but before we acked it) is a normal,
// expected occurrence, not a bug to eliminate upstream. This is what lets the consumer treat
// duplicates as a no-op instead of double-acting on them.
public interface IProcessedEventStore
{
    // Returns true the first time eventId is seen (and records it); false if it was already
    // seen — a duplicate delivery that should be acked without being acted on again.
    Task<bool> TryMarkProcessedAsync(Guid eventId, CancellationToken cancellationToken);
}
