namespace OrdersService.Infrastructure;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 20;

    // At the default 2s poll interval, 20 attempts is ~40s of retrying before a message is
    // given up on — comfortably past the ~12s (6 attempts) a real RabbitMQ container restart
    // took in verification (see CLAUDE.md, step 7.6), with margin for a slower one. A message
    // that's still failing after that many attempts is far more likely to be structurally
    // unpublishable than transiently unlucky.
    public int MaxAttempts { get; init; } = 20;
}
