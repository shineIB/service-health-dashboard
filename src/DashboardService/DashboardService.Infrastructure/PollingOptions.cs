namespace DashboardService.Infrastructure;

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    public int IntervalSeconds { get; init; } = 5;
    public int PerServiceTimeoutSeconds { get; init; } = 2;
}
