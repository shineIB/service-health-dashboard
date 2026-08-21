using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OrdersService.Api.Tests;

// No real Postgres is available for these WebApplicationFactory-based tests, so startup
// migrations must be disabled here. Testcontainers-based integration tests (step 2) will
// cover the real migration/DB path.
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false"
            });
        });
    }
}
