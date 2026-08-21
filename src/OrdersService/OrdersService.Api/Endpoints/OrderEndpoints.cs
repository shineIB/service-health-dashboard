using Microsoft.AspNetCore.Http.HttpResults;
using OrdersService.Api.Contracts;
using OrdersService.Domain;

namespace OrdersService.Api.Endpoints;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateOrder);
        group.MapGet("/{id:guid}", GetOrderById);
        group.MapGet("/", GetOrders);
        group.MapPost("/{id:guid}/confirm", ConfirmOrder);
        group.MapPost("/{id:guid}/cancel", CancelOrder);

        return group;
    }

    // DomainException is not caught here: it propagates to DomainExceptionHandler,
    // which maps it to a 400 response centrally.
    private static async Task<Created<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        IOrderRepository repository,
        CancellationToken cancellationToken)
    {
        var lines = (request.Items ?? []).Select(i => new OrderLine(i.ProductId, i.Quantity, i.UnitPrice));
        var order = Order.Create(request.CustomerId, lines);

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var response = OrderResponse.FromDomain(order);
        return TypedResults.Created($"/orders/{order.Id}", response);
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrderById(
        Guid id,
        IOrderRepository repository,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken);
        return order is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(OrderResponse.FromDomain(order));
    }

    private static async Task<Ok<List<OrderResponse>>> GetOrders(
        IOrderRepository repository,
        CancellationToken cancellationToken)
    {
        var orders = await repository.GetAllAsync(cancellationToken);
        return TypedResults.Ok(orders.Select(OrderResponse.FromDomain).ToList());
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> ConfirmOrder(
        Guid id,
        IOrderRepository repository,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken);
        if (order is null)
            return TypedResults.NotFound();

        order.Confirm();
        await repository.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(OrderResponse.FromDomain(order));
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> CancelOrder(
        Guid id,
        IOrderRepository repository,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken);
        if (order is null)
            return TypedResults.NotFound();

        order.Cancel();
        await repository.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(OrderResponse.FromDomain(order));
    }
}
