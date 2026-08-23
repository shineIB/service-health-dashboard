using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using InventoryService.Api.Endpoints;
using Xunit;

namespace InventoryService.Api.Tests;

[Collection(InventoryApiCollection.Name)]
public class VersionEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public VersionEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Version_ReturnsBuildInfoFromConfiguration()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<VersionResponse>();
        body.Should().NotBeNull();
        body!.Version.Should().NotBeNullOrWhiteSpace();
        body.GitSha.Should().NotBeNullOrWhiteSpace();
        body.BuildTimeUtc.Should().NotBeNullOrWhiteSpace();
    }
}
