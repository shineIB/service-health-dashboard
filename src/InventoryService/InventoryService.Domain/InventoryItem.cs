namespace InventoryService.Domain;

public sealed class InventoryItem
{
    public Guid ProductId { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    private InventoryItem()
    {
    }

    private InventoryItem(Guid productId, int availableQuantity)
    {
        ProductId = productId;
        AvailableQuantity = availableQuantity;
        ReservedQuantity = 0;
    }

    public static InventoryItem Create(Guid productId, int initialQuantity)
    {
        if (productId == Guid.Empty)
            throw new DomainException("ProductId is required.");

        if (initialQuantity < 0)
            throw new DomainException("Initial quantity cannot be negative.");

        return new InventoryItem(productId, initialQuantity);
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (quantity > AvailableQuantity)
            throw new DomainException(
                $"Insufficient stock for product {ProductId}: requested {quantity}, available {AvailableQuantity}.");

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (quantity > ReservedQuantity)
            throw new DomainException(
                $"Cannot release {quantity}: only {ReservedQuantity} reserved for product {ProductId}.");

        ReservedQuantity -= quantity;
        AvailableQuantity += quantity;
    }
}
