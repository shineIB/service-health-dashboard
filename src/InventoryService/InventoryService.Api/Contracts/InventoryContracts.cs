using InventoryService.Domain;

namespace InventoryService.Api.Contracts;

public sealed record CreateInventoryItemRequest(Guid ProductId, int InitialQuantity);

// OrderId is the idempotency key: a retried reserve for the same order is a no-op.
public sealed record ReserveStockRequest(Guid OrderId, int Quantity);

public sealed record ReleaseStockRequest(Guid OrderId);

public sealed record InventoryItemResponse(Guid ProductId, int AvailableQuantity, int ReservedQuantity)
{
    public static InventoryItemResponse FromDomain(InventoryItem item) => new(
        item.ProductId,
        item.AvailableQuantity,
        item.ReservedQuantity);
}
