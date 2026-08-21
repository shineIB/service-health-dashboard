namespace OrdersService.Api.Configuration;

public sealed class BuildInfoOptions
{
    public const string SectionName = "Build";

    public string Version { get; init; } = "0.1.0-dev";
    public string GitSha { get; init; } = "local";
    public string BuildTimeUtc { get; init; } = "unknown";
}
