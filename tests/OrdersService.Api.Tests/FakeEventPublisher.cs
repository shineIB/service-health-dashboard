using OrdersService.Domain;

namespace OrdersService.Api.Tests;

// Replaces the real IEventPublisher in tests so OrderEndpoints can be exercised without a
// running RabbitMQ — same reasoning as FakeInventoryClient.
public sealed class FakeEventPublisher : IEventPublisher
{
    public List<Guid> CreatedOrderIds { get; } = [];
    public List<Guid> ConfirmedOrderIds { get; } = [];
    public List<Guid> CancelledOrderIds { get; } = [];

    public Task PublishOrderCreatedAsync(Order order, CancellationToken cancellationToken)
    {
        CreatedOrderIds.Add(order.Id);
        return Task.CompletedTask;
    }

    public Task PublishOrderConfirmedAsync(Order order, CancellationToken cancellationToken)
    {
        ConfirmedOrderIds.Add(order.Id);
        return Task.CompletedTask;
    }

    public Task PublishOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        CancelledOrderIds.Add(order.Id);
        return Task.CompletedTask;
    }
}
