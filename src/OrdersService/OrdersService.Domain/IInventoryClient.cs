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

public enum ReleaseStockOutcome
{
    Released,
    Unavailable
}

public sealed record ReleaseStockResult(ReleaseStockOutcome Outcome, string? Message = null)
{
    public static ReleaseStockResult Released() => new(ReleaseStockOutcome.Released);
    public static ReleaseStockResult Unavailable(string message) => new(ReleaseStockOutcome.Unavailable, message);
}

public interface IInventoryClient
{
    // orderId is the idempotency key inventory-service uses to make retries safe.
    Task<ReserveStockResult> ReserveStockAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken);

    // Best-effort: a caller that gets Unavailable back should log it and move on rather
    // than fail its own operation — inventory-service's reservation TTL is the backstop
    // that reclaims the stock even if this call never gets through.
    Task<ReleaseStockResult> ReleaseStockAsync(Guid orderId, Guid productId, CancellationToken cancellationToken);
}
