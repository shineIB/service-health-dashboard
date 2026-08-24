namespace OrdersService.Infrastructure;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 20;
}
