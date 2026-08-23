using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using OrdersService.Api.Contracts;
using OrdersService.Api.Telemetry;
using OrdersService.Domain;

namespace OrdersService.Api.Endpoints;

public static class OrderEndpoints
{
    // ILogger<T> needs a non-static type argument; this exists only to name the log category.
    private sealed class LogCategory;

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
    private static async Task<Results<Created<OrderResponse>, ProblemHttpResult>> CreateOrder(
        CreateOrderRequest request,
        IOrderRepository repository,
        IInventoryClient inventoryClient,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var lines = (request.Items ?? []).Select(i => new OrderLine(i.ProductId, i.Quantity, i.UnitPrice));
        var order = Order.Create(request.CustomerId, lines); // throws before any inventory call, so a malformed request never reserves anything

        // Fail-closed: every line must reserve successfully or the order is rejected.
        // orderId is the same for every line, so a client retry of this whole request
        // replays the same idempotency key inventory-service already saw per line.
        //
        // If a later line fails, earlier lines in this loop stay reserved — no
        // compensating release is issued for them. That's deliberate, not an oversight:
        // a compensating call would itself go through the same inventory-service that
        // may be the reason we're failing, so it isn't guaranteed to succeed either.
        // Every reservation carries a TTL in inventory-service and expires on its own,
        // which reclaims the stock without depending on anything succeeding after this
        // point — including this process staying alive.
        foreach (var line in order.Lines)
        {
            var result = await inventoryClient.ReserveStockAsync(order.Id, line.ProductId, line.Quantity, cancellationToken);

            switch (result.Outcome)
            {
                case ReserveStockOutcome.Unavailable:
                    OrdersTelemetry.OrdersRejected.Add(1, new KeyValuePair<string, object?>("reason", "inventory_unavailable"));
                    httpContext.Response.Headers.RetryAfter = "5";
                    return TypedResults.Problem(
                        detail: result.Message ?? "inventory-service is unavailable.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Inventory temporarily unavailable.");

                case ReserveStockOutcome.InsufficientStock:
                    OrdersTelemetry.OrdersRejected.Add(1, new KeyValuePair<string, object?>("reason", "insufficient_stock"));
                    return TypedResults.Problem(
                        detail: result.Message ?? $"Insufficient stock for product {line.ProductId}.",
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Order rejected: insufficient stock.");
            }
        }

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        OrdersTelemetry.OrdersCreated.Add(1);

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
        IInventoryClient inventoryClient,
        ILogger<LogCategory> logger,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken);
        if (order is null)
            return TypedResults.NotFound();

        order.Cancel();
        await repository.SaveChangesAsync(cancellationToken);

        OrdersTelemetry.OrdersCancelled.Add(1);

        // The cancellation itself is already committed above: releasing the reservation
        // is a best-effort side effect, not a precondition. If it fails, the order stays
        // cancelled anyway — inventory-service's reservation TTL reclaims the stock on
        // its own, the same backstop CreateOrder relies on for the reverse case.
        foreach (var line in order.Lines)
        {
            var result = await inventoryClient.ReleaseStockAsync(order.Id, line.ProductId, cancellationToken);
            if (result.Outcome == ReleaseStockOutcome.Unavailable)
            {
                logger.LogWarning(
                    "Failed to release reservation for order {OrderId}, product {ProductId}: {Message}. " +
                    "The reservation's TTL in inventory-service will reclaim it automatically.",
                    order.Id,
                    line.ProductId,
                    result.Message);
            }
        }

        return TypedResults.Ok(OrderResponse.FromDomain(order));
    }
}
