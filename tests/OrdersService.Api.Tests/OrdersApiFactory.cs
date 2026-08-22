using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrdersService.Domain;

namespace OrdersService.Api.Tests;

// No real Postgres is available for these WebApplicationFactory-based tests, so startup
// migrations must be disabled here. Testcontainers-based integration tests (step 2) will
// cover the real migration/DB path. IInventoryClient is replaced with a fake for the same
// reason: no real inventory-service is running for these tests either.
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    public FakeInventoryClient InventoryClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryClient>();
            services.AddSingleton<IInventoryClient>(InventoryClient);
        });
    }
}
