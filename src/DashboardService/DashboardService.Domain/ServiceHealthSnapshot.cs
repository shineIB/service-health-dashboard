namespace DashboardService.Domain;

// LastSuccessfulCheckUtc/ResponseTimeMs/Version/GitSha/BuildTimeUtc deliberately survive
// an Unreachable poll: when a service goes dark, callers should still be able to see what
// its last known good state was, not lose that information the instant one poll fails.
public sealed record ServiceHealthSnapshot(
    string ServiceName,
    string BaseUrl,
    ServiceHealthStatus Status,
    string? Version,
    string? GitSha,
    string? BuildTimeUtc,
    long? ResponseTimeMs,
    DateTimeOffset? LastSuccessfulCheckUtc,
    string? ErrorMessage);
