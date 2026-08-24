using OrdersService.Infrastructure;

namespace OrdersService.Api.Tests;

// Always fails — used by PoisonOutboxFactory to prove OutboxDispatcher gives up after
// OutboxOptions.MaxAttempts instead of retrying a structurally-unpublishable row forever.
// No real RabbitMQ needed for this: the point is the dispatcher's own attempts/give-up logic,
// not the publish call itself (OutboxDispatcherTests already covers a real publish end to end).
public sealed class FakeOutboxSender : IOutboxSender
{
    public int CallCount;

    public Task PublishAsync(string routingKey, byte[] payload, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        throw new InvalidOperationException("Simulated permanent publish failure.");
    }
}
