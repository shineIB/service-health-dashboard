using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InventoryService.Api.Tests;

// Connection string comes from PostgresContainerFixture (a real Testcontainers Postgres,
// shared per collection), not from appsettings.json. Startup migrations stay disabled —
// the fixture already applied them once before any test in the collection runs.
public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiFactory(PostgresContainerFixture postgres)
    {
        _connectionString = postgres.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false",
                ["Infrastructure:ConnectionString"] = _connectionString
            });
        });
    }
}
