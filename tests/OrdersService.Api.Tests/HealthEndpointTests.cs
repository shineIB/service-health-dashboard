using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OrdersService.Api.Tests;

public class HealthEndpointTests : IClassFixture<OrdersApiFactory>
{
    private readonly OrdersApiFactory _factory;

    public HealthEndpointTests(OrdersApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_DoesNotDependOnDatabase_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
