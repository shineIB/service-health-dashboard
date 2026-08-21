namespace OrdersService.Api.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool RunMigrationsOnStartup { get; init; }
}
