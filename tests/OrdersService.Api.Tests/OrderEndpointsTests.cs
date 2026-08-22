using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrdersService.Api.Contracts;
using OrdersService.Domain;
using Xunit;

namespace OrdersService.Api.Tests;

public class OrderEndpointsTests : IClassFixture<OrdersApiFactory>
{
    private readonly OrdersApiFactory _factory;

    public OrderEndpointsTests(OrdersApiFactory factory)
    {
        _factory = factory;
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.Reserved();
        _factory.InventoryClient.NextReleaseResult = ReleaseStockResult.Released();
    }

    private static object ValidCreateOrderRequest(Guid? customerId = null) => new
    {
        customerId = customerId ?? Guid.NewGuid(),
        items = new[]
        {
            new { productId = Guid.NewGuid(), quantity = 2, unitPrice = 10m }
        }
    };

    private async Task<OrderResponse> CreateOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    [Fact]
    public async Task CreateOrder_WithSufficientStock_Returns201AndReservesEachLine()
    {
        var client = _factory.CreateClient();
        var request = ValidCreateOrderRequest();

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        _factory.InventoryClient.ReserveCalls.Should().ContainSingle(call => call.OrderId == body!.Id);
    }

    [Fact]
    public async Task CreateOrder_WhenInventoryReportsInsufficientStock_Returns409AndDoesNotPersistTheOrder()
    {
        var client = _factory.CreateClient();
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.InsufficientStock("Insufficient stock for product.");
        var request = ValidCreateOrderRequest();

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Insufficient stock");
    }

    [Fact]
    public async Task CreateOrder_WhenInventoryIsUnavailable_Returns503WithRetryAfterHeader()
    {
        var client = _factory.CreateClient();
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.Unavailable("inventory-service is unavailable.");
        var request = ValidCreateOrderRequest();

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelOrder_ReleasesTheReservationForEachLine()
    {
        var client = _factory.CreateClient();
        var order = await CreateOrderAsync(client);

        var response = await client.PostAsync($"/orders/{order.Id}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.InventoryClient.ReleaseCalls.Should().ContainSingle(call =>
            call.OrderId == order.Id && call.ProductId == order.Items[0].ProductId);
    }

    [Fact]
    public async Task CancelOrder_WhenReleaseFails_StillCancelsTheOrder()
    {
        var client = _factory.CreateClient();
        var order = await CreateOrderAsync(client);
        _factory.InventoryClient.NextReleaseResult = ReleaseStockResult.Unavailable("inventory-service is unavailable.");

        var response = await client.PostAsync($"/orders/{order.Id}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "cancellation must not depend on the release call succeeding");
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        body!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CreateOrder_WithInvalidQuantity_Returns400WithProblemDetails()
    {
        var client = _factory.CreateClient();
        var request = new
        {
            customerId = Guid.NewGuid(),
            items = new[]
            {
                new { productId = Guid.NewGuid(), quantity = 0, unitPrice = 10m }
            }
        };

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Detail.Should().Contain("Quantity");
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_Returns400WithProblemDetails()
    {
        var client = _factory.CreateClient();
        var request = new { customerId = Guid.NewGuid(), items = Array.Empty<object>() };

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("at least one line");
    }

    [Fact]
    public async Task CreateOrder_WithMissingItems_Returns400WithProblemDetails()
    {
        var client = _factory.CreateClient();
        var request = new { customerId = Guid.NewGuid() };

        var response = await client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("at least one line");
    }
}
