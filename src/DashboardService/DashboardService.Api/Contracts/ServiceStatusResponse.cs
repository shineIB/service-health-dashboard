using DashboardService.Domain;

namespace DashboardService.Api.Contracts;

public sealed record ServiceStatusResponse(
    string ServiceName,
    string BaseUrl,
    string Status,
    string? Version,
    string? GitSha,
    string? BuildTimeUtc,
    long? ResponseTimeMs,
    DateTimeOffset? LastSuccessfulCheckUtc,
    string? ErrorMessage)
{
    public static ServiceStatusResponse FromSnapshot(ServiceHealthSnapshot snapshot) => new(
        snapshot.ServiceName,
        snapshot.BaseUrl,
        snapshot.Status.ToString(),
        snapshot.Version,
        snapshot.GitSha,
        snapshot.BuildTimeUtc,
        snapshot.ResponseTimeMs,
        snapshot.LastSuccessfulCheckUtc,
        snapshot.ErrorMessage);
}
