using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InventoryService.Api.Tests;

// Separate factory (short TTL, fast sweep) so ReservationExpiryTests can observe the
// background sweep actually running, without slowing down every other test by using
// the same short interval everywhere. Shares the collection's Postgres container (via
// PostgresContainerFixture) rather than starting its own.
public class ReservationExpiryFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ReservationExpiryFactory(PostgresContainerFixture postgres)
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
                ["Infrastructure:ConnectionString"] = _connectionString,
                ["Reservation:TtlSeconds"] = "1",
                ["Reservation:ExpirySweepIntervalSeconds"] = "1"
            });
        });
    }
}
