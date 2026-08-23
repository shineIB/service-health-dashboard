using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrdersService.Domain;
using Xunit;

namespace OrdersService.Api.Tests;

// Presence checks only, never exact counter values: OrdersTelemetry's Meter is a static,
// process-wide instrument, so every WebApplicationFactory in this test assembly (one per
// test class) observes the same underlying counters. Asserting "the metric we just triggered
// is now in the scrape output" is robust to that; asserting an exact count would be flaky
// against whatever else is running concurrently in the same test process.
[Collection(OrdersApiCollection.Name)]
public class MetricsEndpointTests : IClassFixture<OrdersApiFactory>
{
    private readonly OrdersApiFactory _factory;

    public MetricsEndpointTests(OrdersApiFactory factory)
    {
        _factory = factory;
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.Reserved();
    }

    private static object ValidCreateOrderRequest() => new
    {
        customerId = Guid.NewGuid(),
        items = new[]
        {
            new { productId = Guid.NewGuid(), quantity = 1, unitPrice = 5m }
        }
    };

    // The very first measurement ever recorded on a given custom instrument isn't always
    // visible in the /metrics scrape that immediately follows it (observed directly: back-to-
    // back requests race against the OTel SDK's own instrument-publish bookkeeping; a real
    // Kestrel run with a few seconds between "create the order" and "curl /metrics" never
    // showed this, only these two calls made back-to-back in-process). Auto-instrumentation
    // (already registered at host startup, not lazily on first use) never needs this. Polling
    // for a couple hundred ms is deterministic in practice and avoids a flaky one-shot read.
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
    public async Task Metrics_AfterOrderIsCreated_ExposesOrdersCreatedCounter()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var metrics = await ScrapeMetricsUntilAsync(client, "orders_created_total");
        metrics.Should().Contain("orders_created_total");
    }

    [Fact]
    public async Task Metrics_AfterOrderIsRejectedForInsufficientStock_ExposesOrdersRejectedCounterWithReason()
    {
        var client = _factory.CreateClient();
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.InsufficientStock("out of stock");

        var createResponse = await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var metrics = await ScrapeMetricsUntilAsync(client, "orders_rejected_total");
        metrics.Should().Contain("orders_rejected_total");
        metrics.Should().Contain("insufficient_stock");
    }
}
