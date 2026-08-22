using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using InventoryService.Api.Contracts;
using Xunit;

namespace InventoryService.Api.Tests;

public class InventoryEndpointsTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public InventoryEndpointsTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateItem_WithValidData_Returns201WithFullyAvailableStock()
    {
        var client = _factory.CreateClient();
        var request = new { productId = Guid.NewGuid(), initialQuantity = 50 };

        var response = await client.PostAsJsonAsync("/inventory", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<InventoryItemResponse>();
        body!.AvailableQuantity.Should().Be(50);
        body.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task CreateItem_ForProductThatAlreadyExists_Returns400WithProblemDetails()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });

        var response = await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("already exists");
    }

    [Fact]
    public async Task GetItemByProductId_ForUnknownProduct_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/inventory/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reserve_WithSufficientStock_MovesQuantityFromAvailableToReserved()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });

        var response = await client.PostAsJsonAsync(
            $"/inventory/{productId}/reserve",
            new { orderId = Guid.NewGuid(), quantity = 4 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<InventoryItemResponse>();
        body!.AvailableQuantity.Should().Be(6);
        body.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public async Task Reserve_CalledTwiceWithSameOrderId_IsIdempotentAndDoesNotDoubleReserve()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });

        var first = await client.PostAsJsonAsync($"/inventory/{productId}/reserve", new { orderId, quantity = 4 });
        var second = await client.PostAsJsonAsync($"/inventory/{productId}/reserve", new { orderId, quantity = 4 });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await second.Content.ReadFromJsonAsync<InventoryItemResponse>();
        body!.AvailableQuantity.Should().Be(6, "a retried reserve for the same order must not reserve twice");
        body.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public async Task Reserve_WithInsufficientStock_Returns409WithProblemDetails()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 2 });

        var response = await client.PostAsJsonAsync(
            $"/inventory/{productId}/reserve",
            new { orderId = Guid.NewGuid(), quantity = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Insufficient stock");
    }

    [Fact]
    public async Task Reserve_ForUnknownProduct_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/inventory/{Guid.NewGuid()}/reserve",
            new { orderId = Guid.NewGuid(), quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Release_WithActiveReservation_MovesQuantityBackToAvailable()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });
        await client.PostAsJsonAsync($"/inventory/{productId}/reserve", new { orderId, quantity = 4 });

        var response = await client.PostAsJsonAsync($"/inventory/{productId}/release", new { orderId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<InventoryItemResponse>();
        body!.AvailableQuantity.Should().Be(10);
        body.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Release_ForOrderWithNoReservation_IsANoOpReturning200()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });

        var response = await client.PostAsJsonAsync($"/inventory/{productId}/release", new { orderId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Release_ForUnknownProduct_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/inventory/{Guid.NewGuid()}/release",
            new { orderId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
