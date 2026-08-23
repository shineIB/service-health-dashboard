using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InventoryService.Api.Telemetry;

// Manual spans for the domain operations that matter most in a trace: reserve and release.
// Auto-instrumentation already covers the HTTP request and the Npgsql query around them, but
// a span named for the actual business operation, tagged with order.id and product.id, is
// what lets a trace answer "which order reserved which product" without cross-referencing logs.
//
// Same reasoning extends to the Meter below: request-count/latency comes free from
// auto-instrumentation, but "how many reservations actually succeeded vs. got rejected for
// insufficient stock" is a business outcome, not an HTTP outcome (both are still 2xx/4xx from
// the framework's point of view at different layers).
public static class InventoryTelemetry
{
    public const string ActivitySourceName = "InventoryService.Api";
    public const string MeterName = "InventoryService.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ReservationsSucceeded =
        Meter.CreateCounter<long>("inventory.reservations.succeeded", description: "Number of stock reservations that succeeded (including idempotent replays).");

    public static readonly Counter<long> ReservationsFailed =
        Meter.CreateCounter<long>("inventory.reservations.failed", description: "Number of stock reservations rejected for insufficient stock.");

    public static readonly Counter<long> Releases =
        Meter.CreateCounter<long>("inventory.releases", description: "Number of stock releases.");
}
