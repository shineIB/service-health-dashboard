namespace OrdersService.Domain;

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(l => l.LineTotal);

    private Order()
    {
    }

    private Order(Guid id, Guid customerId, IEnumerable<OrderLine> lines, DateTimeOffset createdAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        Status = OrderStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        _lines.AddRange(lines);
    }

    public static Order Create(Guid customerId, IEnumerable<OrderLine> lines)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("CustomerId is required.");

        var lineList = lines.ToList();
        if (lineList.Count == 0)
            throw new DomainException("An order must contain at least one line.");

        return new Order(Guid.NewGuid(), customerId, lineList, DateTimeOffset.UtcNow);
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException($"Cannot confirm an order in status {Status}.");

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new DomainException("Order is already cancelled.");

        if (Status == OrderStatus.Confirmed)
            throw new DomainException("Cannot cancel a confirmed order.");

        Status = OrderStatus.Cancelled;
    }
}
