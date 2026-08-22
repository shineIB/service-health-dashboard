namespace DashboardService.Infrastructure;

// Local shape matching each monitored service's /version response
// (see OrdersService.Api/Endpoints/VersionEndpoint.cs and its inventory-service twin).
internal sealed record VersionPayload(string? Version, string? GitSha, string? BuildTimeUtc);
