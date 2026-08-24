using System.Text.Json;
using OrdersService.Domain;

namespace OrdersService.Infrastructure;

// Wire shape orders-service publishes (once OutboxDispatcher gets to it). notifications-service
// keeps its own, separately deserialized copy of this shape (OrderEventEnvelope) rather than
// referencing this type — same "local shape per service" principle as InventoryClient's
// ProblemDetailsPayload, so the two services stay independently deployable.
internal sealed record OrderEventPayload(
    Guid EventId,
    string EventType,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAtUtc);

// Adds an OutboxMessage to the current OrdersDbContext's change tracker — no I/O, no
// SaveChangesAsync call here. The caller (OrderEndpoints) must Enqueue* before its own
// IOrderRepository.SaveChangesAsync so the order and the outbox row commit in the same
// transaction. Relies on OrderRepository and this class sharing the same scoped OrdersDbContext
// instance within a request, which the DI container already guarantees (both are Scoped).
public sealed class EfOrderEventOutbox : IOrderEventOutbox
{
    private readonly OrdersDbContext _dbContext;

    public EfOrderEventOutbox(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void EnqueueOrderCreated(Order order) => Enqueue("order.created", order);
    public void EnqueueOrderConfirmed(Order order) => Enqueue("order.confirmed", order);
    public void EnqueueOrderCancelled(Order order) => Enqueue("order.cancelled", order);

    private void Enqueue(string eventType, Order order)
    {
        // The row's own Id doubles as the event's stable identity across every future publish
        // attempt (including retries after a crash) — generated once, here, not re-generated
        // by OutboxDispatcher on each attempt. That stability is what lets notifications-service
        // dedupe a redelivered event by EventId.
        var id = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;

        var payload = new OrderEventPayload(id, eventType, order.Id, order.CustomerId, occurredAtUtc);

        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            OrderId = order.Id,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = occurredAtUtc,
        });
    }
}
