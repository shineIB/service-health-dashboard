using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DashboardService.Api.Tests;

// Removes ServiceHealthPollingService: tests seed IServiceHealthCache directly and need
// the real background poller (which would hit http://localhost:8080 etc. from
// appsettings.json every few seconds) to not overwrite what they set up mid-test.
public class DashboardApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}
