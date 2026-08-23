using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrdersService.Domain;

namespace OrdersService.Api.Tests;

// Connection string comes from PostgresContainerFixture (a real Testcontainers Postgres,
// shared per collection), not from appsettings.json. Startup migrations stay disabled —
// the fixture already applied them once before any test in the collection runs.
// IInventoryClient is still replaced with a fake: no real inventory-service runs for these
// tests, only its own database is real.
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public FakeInventoryClient InventoryClient { get; } = new();

    public OrdersApiFactory(PostgresContainerFixture postgres)
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

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryClient>();
            services.AddSingleton<IInventoryClient>(InventoryClient);
        });
    }
}
