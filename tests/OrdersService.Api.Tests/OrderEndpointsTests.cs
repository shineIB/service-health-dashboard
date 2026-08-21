using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace OrdersService.Api.Tests;

public class OrderEndpointsTests : IClassFixture<OrdersApiFactory>
{
    private readonly OrdersApiFactory _factory;

    public OrderEndpointsTests(OrdersApiFactory factory)
    {
        _factory = factory;
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
