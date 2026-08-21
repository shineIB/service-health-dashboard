namespace OrdersService.Infrastructure;

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    public required string ConnectionString { get; init; }
}
