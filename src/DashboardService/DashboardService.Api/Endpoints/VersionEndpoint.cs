using Microsoft.Extensions.Options;
using DashboardService.Api.Configuration;

namespace DashboardService.Api.Endpoints;

public sealed record VersionResponse(string Version, string GitSha, string BuildTimeUtc);

public static class VersionEndpoint
{
    public static IEndpointRouteBuilder MapVersionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/version", (IOptions<BuildInfoOptions> options) =>
        {
            var info = options.Value;
            return TypedResults.Ok(new VersionResponse(info.Version, info.GitSha, info.BuildTimeUtc));
        });

        return app;
    }
}
