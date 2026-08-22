using InventoryService.Domain;

namespace InventoryService.Api.Contracts;

public sealed record CreateInventoryItemRequest(Guid ProductId, int InitialQuantity);

public sealed record ReserveStockRequest(int Quantity);

public sealed record ReleaseStockRequest(int Quantity);

public sealed record InventoryItemResponse(Guid ProductId, int AvailableQuantity, int ReservedQuantity)
{
    public static InventoryItemResponse FromDomain(InventoryItem item) => new(
        item.ProductId,
        item.AvailableQuantity,
        item.ReservedQuantity);
}
