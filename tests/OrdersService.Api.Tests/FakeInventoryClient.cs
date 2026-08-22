using OrdersService.Domain;

namespace OrdersService.Api.Tests;

// Replaces the real IInventoryClient in tests so OrderEndpoints can be exercised without
// a running inventory-service. NextReserveResult/NextReleaseResult are mutable per test:
// xUnit runs the test methods of a single class (and therefore a single shared
// OrdersApiFactory/singleton instance) sequentially by default, so setting them before
// each HTTP call is safe.
public sealed class FakeInventoryClient : IInventoryClient
{
    public ReserveStockResult NextReserveResult { get; set; } = ReserveStockResult.Reserved();
    public ReleaseStockResult NextReleaseResult { get; set; } = ReleaseStockResult.Released();

    public List<(Guid OrderId, Guid ProductId, int Quantity)> ReserveCalls { get; } = [];
    public List<(Guid OrderId, Guid ProductId)> ReleaseCalls { get; } = [];

    public Task<ReserveStockResult> ReserveStockAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken)
    {
        ReserveCalls.Add((orderId, productId, quantity));
        return Task.FromResult(NextReserveResult);
    }

    public Task<ReleaseStockResult> ReleaseStockAsync(Guid orderId, Guid productId, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add((orderId, productId));
        return Task.FromResult(NextReleaseResult);
    }
}
