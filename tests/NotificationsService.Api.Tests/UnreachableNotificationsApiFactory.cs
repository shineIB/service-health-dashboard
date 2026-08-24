using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NotificationsService.Api.Tests;

// No container involved — points at a closed local port so RabbitMqConnectionProvider's
// connect attempt fails fast (connection refused) instead of timing out against an
// unroutable address. Used to exercise the "RabbitMQ unreachable" side of /health/ready
// without touching the shared RabbitMqContainerFixture other tests depend on.
public class UnreachableNotificationsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:HostName"] = "localhost",
                ["RabbitMq:Port"] = "1"
            });
        });
    }
}
