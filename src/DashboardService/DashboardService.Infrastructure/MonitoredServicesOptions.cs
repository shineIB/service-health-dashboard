namespace DashboardService.Infrastructure;

// Config-driven, not hardcoded and not from the k8s API (yet — see CLAUDE.md for why
// k8s-native service discovery is the natural next step here, not this iteration).
public sealed class MonitoredServicesOptions
{
    public const string SectionName = "MonitoredServices";

    public List<MonitoredServiceEntry> Services { get; init; } = [];
}

public sealed class MonitoredServiceEntry
{
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
}
