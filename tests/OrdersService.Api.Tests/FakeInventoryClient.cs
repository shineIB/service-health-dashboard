using OrdersService.Domain;

namespace OrdersService.Api.Tests;

// Replaces the real IInventoryClient in tests so OrderEndpoints can be exercised without
// a running inventory-service. NextResult is mutable per test: xUnit runs the test methods
// of a single class (and therefore a single shared OrdersApiFactory/singleton instance)
// sequentially by default, so setting it before each HTTP call is safe.
public sealed class FakeInventoryClient : IInventoryClient
{
    public ReserveStockResult NextResult { get; set; } = ReserveStockResult.Reserved();

    public List<(Guid OrderId, Guid ProductId, int Quantity)> Calls { get; } = [];

    public Task<ReserveStockResult> ReserveStockAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken)
    {
        Calls.Add((orderId, productId, quantity));
        return Task.FromResult(NextResult);
    }
}
