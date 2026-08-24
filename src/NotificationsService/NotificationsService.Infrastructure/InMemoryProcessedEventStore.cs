using System.Collections.Concurrent;
using NotificationsService.Domain;

namespace NotificationsService.Infrastructure;

// In-memory, not persisted: notifications-service deliberately has no database (see CLAUDE.md,
// step 7). Trade-off, stated plainly rather than hidden — this dedupe window does not survive
// a process restart, so a message redelivered *after* a restart (RabbitMQ requeues an unacked
// delivery, the pod comes back up, and only then does the redelivery land) would be processed
// again. Acceptable here because the only side effect is a log line (LoggingNotificationSender),
// not a real action; a persistent store would be the right call the moment that side effect
// becomes one that must never repeat.
//
// TryAdd is atomic, so concurrent deliveries of the same EventId can't both win — same
// correctness property inventory-service's idempotency-by-orderId reservation lookup relies on.
public sealed class InMemoryProcessedEventStore : IProcessedEventStore
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seenAt = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryProcessedEventStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<bool> TryMarkProcessedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var isNew = _seenAt.TryAdd(eventId, now);

        Prune(now);

        return Task.FromResult(isNew);
    }

    // Opportunistic, on every call, instead of a separate sweep loop: event volume here is low
    // enough that scanning the whole dictionary each time is cheap, and it avoids a second
    // BackgroundService just to bound memory.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (eventId, seenAt) in _seenAt)
        {
            if (now - seenAt > RetentionWindow)
                _seenAt.TryRemove(eventId, out _);
        }
    }
}
