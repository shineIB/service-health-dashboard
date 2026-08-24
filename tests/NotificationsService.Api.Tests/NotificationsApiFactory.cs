using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NotificationsService.Api.Tests;

// Points RabbitMq:HostName/Port at the real Testcontainers RabbitMQ from RabbitMqContainerFixture
// instead of appsettings.json's localhost:5672. OrderEventConsumer (a real IHostedService) is
// left running against it — /health/ready reflects a real connection, not a fake.
public class NotificationsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _hostName;
    private readonly int _port;

    public NotificationsApiFactory(RabbitMqContainerFixture rabbitMq)
    {
        _hostName = rabbitMq.HostName;
        _port = rabbitMq.Port;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:HostName"] = _hostName,
                ["RabbitMq:Port"] = _port.ToString()
            });
        });
    }
}
