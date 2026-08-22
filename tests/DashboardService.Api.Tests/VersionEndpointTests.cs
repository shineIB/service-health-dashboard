using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using DashboardService.Api.Endpoints;
using Xunit;

namespace DashboardService.Api.Tests;

public class VersionEndpointTests : IClassFixture<DashboardApiFactory>
{
    private readonly DashboardApiFactory _factory;

    public VersionEndpointTests(DashboardApiFactory factory)
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
