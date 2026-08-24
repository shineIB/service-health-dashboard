namespace OrdersService.Infrastructure;

// Infrastructure-only seam (not Domain — nothing in Api or Domain ever depends on this;
// OutboxDispatcher is the only caller) that exists so tests can swap RabbitMqOutboxSender for a
// fake that fails on demand, the same way FakeInventoryClient/FakeNotificationSender stand in
// for their real HTTP/RabbitMQ counterparts elsewhere in this solution.
public interface IOutboxSender
{
    Task PublishAsync(string routingKey, byte[] payload, CancellationToken cancellationToken);
}
