using System.Diagnostics;

namespace InventoryService.Api.Telemetry;

// Manual spans for the domain operations that matter most in a trace: reserve and release.
// Auto-instrumentation already covers the HTTP request and the Npgsql query around them, but
// a span named for the actual business operation, tagged with order.id and product.id, is
// what lets a trace answer "which order reserved which product" without cross-referencing logs.
public static class InventoryTelemetry
{
    public const string ActivitySourceName = "InventoryService.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
