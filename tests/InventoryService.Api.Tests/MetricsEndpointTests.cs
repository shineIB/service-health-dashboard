using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace InventoryService.Api.Tests;

// Presence checks only, never exact counter values — same reasoning as
// OrdersService.Api.Tests.MetricsEndpointTests: InventoryTelemetry's Meter is static and
// process-wide, shared across every WebApplicationFactory in this test assembly.
[Collection(InventoryApiCollection.Name)]
public class MetricsEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public MetricsEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    // See OrdersService.Api.Tests.MetricsEndpointTests.ScrapeMetricsUntilAsync for why this
    // exists: the first-ever measurement on a brand new custom instrument isn't always visible
    // in the /metrics scrape that immediately follows it, purely from being called back-to-back
    // in-process. Auto-instrumentation (registered at host startup) never needs this.
    private static async Task<string> ScrapeMetricsUntilAsync(HttpClient client, string expectedSubstring)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        var last = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            last = await (await client.GetAsync("/metrics")).Content.ReadAsStringAsync();
            if (last.Contains(expectedSubstring, StringComparison.Ordinal))
                break;

            await Task.Delay(50);
        }

        return last;
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusTextExposingAspNetCoreInstrumentation()
    {
        var client = _factory.CreateClient();
        await client.GetAsync("/version");

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("http_server_request_duration");
    }

    [Fact]
    public async Task Metrics_AfterReserveSucceeds_ExposesReservationsSucceededCounter()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });

        var reserveResponse = await client.PostAsJsonAsync(
            $"/inventory/{productId}/reserve",
            new { orderId = Guid.NewGuid(), quantity = 1 });
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var metrics = await ScrapeMetricsUntilAsync(client, "inventory_reservations_succeeded_total");
        metrics.Should().Contain("inventory_reservations_succeeded_total");
    }

    [Fact]
    public async Task Metrics_AfterReserveFailsForInsufficientStock_ExposesReservationsFailedCounter()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 1 });

        var reserveResponse = await client.PostAsJsonAsync(
            $"/inventory/{productId}/reserve",
            new { orderId = Guid.NewGuid(), quantity = 100 });
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var metrics = await ScrapeMetricsUntilAsync(client, "inventory_reservations_failed_total");
        metrics.Should().Contain("inventory_reservations_failed_total");
    }

    [Fact]
    public async Task Metrics_AfterReleaseSucceeds_ExposesReleasesCounter()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });
        await client.PostAsJsonAsync($"/inventory/{productId}/reserve", new { orderId, quantity = 4 });

        var releaseResponse = await client.PostAsJsonAsync($"/inventory/{productId}/release", new { orderId });
        releaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var metrics = await ScrapeMetricsUntilAsync(client, "inventory_releases_total");
        metrics.Should().Contain("inventory_releases_total");
    }
}
