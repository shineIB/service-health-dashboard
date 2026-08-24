namespace OrdersService.Domain;

// Stages an order-lifecycle event for reliable delivery — it does not publish anything itself.
// Implementations must write within the SAME database transaction as the order change that
// triggered the event: call Enqueue*, then a single IOrderRepository.SaveChangesAsync commits
// both together. A separate BackgroundService (OutboxDispatcher, in Infrastructure) reads
// committed rows and actually publishes to RabbitMQ, independently of any HTTP request — see
// CLAUDE.md, step 7.5, for why this replaced the earlier best-effort direct-publish design.
public interface IOrderEventOutbox
{
    void EnqueueOrderCreated(Order order);
    void EnqueueOrderConfirmed(Order order);
    void EnqueueOrderCancelled(Order order);
}
