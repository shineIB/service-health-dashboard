using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace NotificationsService.Api.Tests;

[Collection(NotificationsApiCollection.Name)]
public class VersionEndpointTests : IClassFixture<NotificationsApiFactory>
{
    private readonly NotificationsApiFactory _factory;

    public VersionEndpointTests(NotificationsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Version_ReturnsDefaultBuildInfo()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["version"].Should().Be("0.1.0-dev");
    }
}
