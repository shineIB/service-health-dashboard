using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InventoryService.Api.Tests;

// Separate factory (short TTL, fast sweep) so ReservationExpiryTests can observe the
// background sweep actually running, without slowing down every other test by using
// the same short interval everywhere.
public class ReservationExpiryFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false",
                ["Reservation:TtlSeconds"] = "1",
                ["Reservation:ExpirySweepIntervalSeconds"] = "1"
            });
        });
    }
}
