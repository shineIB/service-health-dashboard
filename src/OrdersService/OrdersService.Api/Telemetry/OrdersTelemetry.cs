using System.Diagnostics.Metrics;

namespace OrdersService.Api.Telemetry;

// Business counters for the outcomes that actually matter on the dashboard/Grafana side
// later: how many orders got created vs. rejected, and why. Auto-instrumentation already
// covers request counts/latency for every endpoint — these exist because "rejected, and
// was it because of stock or because inventory was down" isn't recoverable from that alone.
public static class OrdersTelemetry
{
    public const string MeterName = "OrdersService.Api";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("orders.created", description: "Number of orders successfully created and reserved.");

    // Tagged with "reason" (insufficient_stock | inventory_unavailable) at the call site.
    public static readonly Counter<long> OrdersRejected =
        Meter.CreateCounter<long>("orders.rejected", description: "Number of orders rejected, tagged by reason.");

    public static readonly Counter<long> OrdersCancelled =
        Meter.CreateCounter<long>("orders.cancelled", description: "Number of orders cancelled.");
}
