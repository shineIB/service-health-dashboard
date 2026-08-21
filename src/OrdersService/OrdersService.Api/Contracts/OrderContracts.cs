using OrdersService.Domain;

namespace OrdersService.Api.Contracts;

public sealed record CreateOrderRequest(Guid CustomerId, List<CreateOrderLineRequest> Items);

public sealed record CreateOrderLineRequest(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAtUtc,
    List<OrderLineResponse> Items)
{
    public static OrderResponse FromDomain(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status.ToString(),
        order.Total,
        order.CreatedAtUtc,
        order.Lines.Select(OrderLineResponse.FromDomain).ToList());
}

public sealed record OrderLineResponse(Guid ProductId, int Quantity, decimal UnitPrice, decimal LineTotal)
{
    public static OrderLineResponse FromDomain(OrderLine line) => new(
        line.ProductId,
        line.Quantity,
        line.UnitPrice,
        line.LineTotal);
}
