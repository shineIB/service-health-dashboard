namespace DashboardService.Domain;

public enum ServiceHealthStatus
{
    Healthy,
    Unhealthy,

    // No HTTP response at all (timeout, connection refused, DNS failure) — distinct
    // from Unhealthy (the service responded, just not with success) because they mean
    // different things to whoever is troubleshooting: "it's up but degraded" vs.
    // "it's not there at all."
    Unreachable
}
