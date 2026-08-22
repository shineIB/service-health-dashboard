namespace InventoryService.Domain;

public sealed class InventoryItem
{
    private readonly List<Reservation> _reservations = [];

    public Guid ProductId { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public IReadOnlyCollection<Reservation> Reservations => _reservations.AsReadOnly();

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

    // orderId is the idempotency key: a retried reserve for the same order is a
    // no-op that returns the already-reserved state instead of reserving twice.
    public void Reserve(Guid orderId, int quantity, TimeSpan ttl, DateTimeOffset nowUtc)
    {
        ExpireStaleReservations(nowUtc);

        if (_reservations.Any(r => r.OrderId == orderId))
            return;

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (quantity > AvailableQuantity)
            throw new InsufficientStockException(
                $"Insufficient stock for product {ProductId}: requested {quantity}, available {AvailableQuantity}.");

        _reservations.Add(new Reservation(orderId, ProductId, quantity, nowUtc, nowUtc + ttl));
        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
    }

    // Idempotent by nature: releasing an order with no active reservation (already
    // released, already expired, or never reserved) is a no-op, not an error.
    public void Release(Guid orderId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.OrderId == orderId);
        if (reservation is null)
            return;

        _reservations.Remove(reservation);
        ReservedQuantity -= reservation.Quantity;
        AvailableQuantity += reservation.Quantity;
    }

    // Returns the number of reservations released back to available stock, so the
    // background sweep can log it without a second pass over the collection.
    public int ExpireStaleReservations(DateTimeOffset nowUtc)
    {
        var expired = _reservations.Where(r => r.ExpiresAtUtc <= nowUtc).ToList();
        foreach (var reservation in expired)
        {
            _reservations.Remove(reservation);
            ReservedQuantity -= reservation.Quantity;
            AvailableQuantity += reservation.Quantity;
        }

        return expired.Count;
    }
}
