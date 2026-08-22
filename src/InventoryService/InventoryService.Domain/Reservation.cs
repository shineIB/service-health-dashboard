namespace InventoryService.Domain;

// One reservation per (ProductId, OrderId): OrderId doubles as the idempotency key
// that lets InventoryItem.Reserve recognize a retried request and return the same
// result instead of reserving stock twice.
public sealed class Reservation
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private Reservation()
    {
    }

    internal Reservation(Guid orderId, Guid productId, int quantity, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }
}
