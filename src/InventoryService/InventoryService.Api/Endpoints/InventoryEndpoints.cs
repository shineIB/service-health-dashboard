using Microsoft.AspNetCore.Http.HttpResults;
using InventoryService.Api.Contracts;
using InventoryService.Domain;

namespace InventoryService.Api.Endpoints;

public static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateItem);
        group.MapGet("/{productId:guid}", GetItemByProductId);
        group.MapGet("/", GetItems);
        group.MapPost("/{productId:guid}/reserve", ReserveStock);
        group.MapPost("/{productId:guid}/release", ReleaseStock);

        return group;
    }

    // DomainException is not caught here: it propagates to DomainExceptionHandler,
    // which maps it to a 400 response centrally.
    private static async Task<Created<InventoryItemResponse>> CreateItem(
        CreateInventoryItemRequest request,
        IInventoryRepository repository,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByProductIdAsync(request.ProductId, cancellationToken);
        if (existing is not null)
            throw new DomainException($"An inventory item for product {request.ProductId} already exists.");

        var item = InventoryItem.Create(request.ProductId, request.InitialQuantity);

        await repository.AddAsync(item, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var response = InventoryItemResponse.FromDomain(item);
        return TypedResults.Created($"/inventory/{item.ProductId}", response);
    }

    private static async Task<Results<Ok<InventoryItemResponse>, NotFound>> GetItemByProductId(
        Guid productId,
        IInventoryRepository repository,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(productId, cancellationToken);
        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(InventoryItemResponse.FromDomain(item));
    }

    private static async Task<Ok<List<InventoryItemResponse>>> GetItems(
        IInventoryRepository repository,
        CancellationToken cancellationToken)
    {
        var items = await repository.GetAllAsync(cancellationToken);
        return TypedResults.Ok(items.Select(InventoryItemResponse.FromDomain).ToList());
    }

    private static async Task<Results<Ok<InventoryItemResponse>, NotFound>> ReserveStock(
        Guid productId,
        ReserveStockRequest request,
        IInventoryRepository repository,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(productId, cancellationToken);
        if (item is null)
            return TypedResults.NotFound();

        item.Reserve(request.Quantity);
        await repository.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(InventoryItemResponse.FromDomain(item));
    }

    private static async Task<Results<Ok<InventoryItemResponse>, NotFound>> ReleaseStock(
        Guid productId,
        ReleaseStockRequest request,
        IInventoryRepository repository,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(productId, cancellationToken);
        if (item is null)
            return TypedResults.NotFound();

        item.Release(request.Quantity);
        await repository.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(InventoryItemResponse.FromDomain(item));
    }
}
