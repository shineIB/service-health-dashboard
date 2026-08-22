using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DashboardService.Domain;

namespace DashboardService.Infrastructure;

// Split out from ServiceHealthPollingService so the actual check logic — the part with
// real branching to get right (Healthy vs Unhealthy vs Unreachable, what survives a
// failed poll) — is directly unit-testable without running a BackgroundService/timer loop.
public sealed class ServiceHealthChecker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public ServiceHealthChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceHealthSnapshot> CheckAsync(
        MonitoredServiceEntry service,
        ServiceHealthSnapshot? previous,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var healthResponse = await _httpClient.GetAsync($"{service.BaseUrl}/health/ready", cts.Token);
            stopwatch.Stop();

            var status = healthResponse.IsSuccessStatusCode
                ? ServiceHealthStatus.Healthy
                : ServiceHealthStatus.Unhealthy;

            // Best-effort, on whatever's left of the same timeout budget: a service that
            // answered /health/ready is "successfully checked" regardless of what
            // /version does. A failure here must not downgrade Healthy/Unhealthy to
            // Unreachable, and just keeps whatever version info we last had.
            var (version, gitSha, buildTimeUtc) = (previous?.Version, previous?.GitSha, previous?.BuildTimeUtc);
            try
            {
                var payload = await _httpClient.GetFromJsonAsync<VersionPayload>(
                    $"{service.BaseUrl}/version", JsonOptions, cts.Token);
                if (payload is not null)
                {
                    (version, gitSha, buildTimeUtc) = (payload.Version, payload.GitSha, payload.BuildTimeUtc);
                }
            }
            catch
            {
                // Intentionally swallowed — see comment above.
            }

            return new ServiceHealthSnapshot(
                service.Name,
                service.BaseUrl,
                status,
                version,
                gitSha,
                buildTimeUtc,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow,
                status == ServiceHealthStatus.Unhealthy
                    ? $"Responded with HTTP {(int)healthResponse.StatusCode}."
                    : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // No response at all (timeout, connection refused, DNS failure): Unreachable.
            // LastSuccessfulCheckUtc/version fields carry over from `previous` untouched —
            // a service going dark shouldn't erase what we last knew about it.
            return new ServiceHealthSnapshot(
                service.Name,
                service.BaseUrl,
                ServiceHealthStatus.Unreachable,
                previous?.Version,
                previous?.GitSha,
                previous?.BuildTimeUtc,
                null,
                previous?.LastSuccessfulCheckUtc,
                ex.Message);
        }
    }
}
