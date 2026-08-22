using DashboardService.Api.Contracts;
using DashboardService.Domain;

namespace DashboardService.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // Reads the cache only — never calls out to a monitored service on this request
        // path. See ServiceHealthPollingService for why that separation matters.
        app.MapGet("/api/services", (IServiceHealthCache cache) =>
            TypedResults.Ok(cache.GetAll().Select(ServiceStatusResponse.FromSnapshot).ToList()));

        return app;
    }
}
