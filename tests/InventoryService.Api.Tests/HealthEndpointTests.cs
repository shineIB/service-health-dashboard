using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace InventoryService.Api.Tests;

public class HealthEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public HealthEndpointTests(InventoryApiFactory factory)
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
