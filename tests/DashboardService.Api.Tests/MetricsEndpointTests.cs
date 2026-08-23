using System.Net;
using FluentAssertions;
using Xunit;

namespace DashboardService.Api.Tests;

public class MetricsEndpointTests : IClassFixture<DashboardApiFactory>
{
    private readonly DashboardApiFactory _factory;

    public MetricsEndpointTests(DashboardApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusTextExposingAspNetCoreInstrumentation()
    {
        var client = _factory.CreateClient();
        // Any prior request is enough to have recorded at least one http.server.request.duration
        // measurement before this scrape.
        await client.GetAsync("/version");

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("http_server_request_duration");
    }
}
