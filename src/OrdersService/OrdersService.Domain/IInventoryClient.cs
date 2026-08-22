namespace OrdersService.Domain;

public enum ReserveStockOutcome
{
    Reserved,
    InsufficientStock,
    Unavailable
}

public sealed record ReserveStockResult(ReserveStockOutcome Outcome, string? Message = null)
{
    public static ReserveStockResult Reserved() => new(ReserveStockOutcome.Reserved);
    public static ReserveStockResult InsufficientStock(string message) => new(ReserveStockOutcome.InsufficientStock, message);
    public static ReserveStockResult Unavailable(string message) => new(ReserveStockOutcome.Unavailable, message);
}

public interface IInventoryClient
{
    // orderId is the idempotency key inventory-service uses to make retries safe.
    Task<ReserveStockResult> ReserveStockAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken);
}
