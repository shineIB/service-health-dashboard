namespace OrdersService.Domain;

// Best-effort: implementations must never throw. A publish failure is logged and counted
// internally (see RabbitMqEventPublisher) but must never change the outcome of the order
// operation that triggered it — the order is already committed to Postgres by the time any
// of these are called, so failing the HTTP request at that point would be misleading.
public interface IEventPublisher
{
    Task PublishOrderCreatedAsync(Order order, CancellationToken cancellationToken);
    Task PublishOrderConfirmedAsync(Order order, CancellationToken cancellationToken);
    Task PublishOrderCancelledAsync(Order order, CancellationToken cancellationToken);
}
