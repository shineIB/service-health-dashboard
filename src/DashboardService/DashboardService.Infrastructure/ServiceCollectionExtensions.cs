using DashboardService.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MonitoredServicesOptions>(configuration.GetSection(MonitoredServicesOptions.SectionName));
        services.Configure<PollingOptions>(configuration.GetSection(PollingOptions.SectionName));

        services.AddHttpClient();
        services.AddSingleton<IServiceHealthCache, InMemoryServiceHealthCache>();
        services.AddHostedService<ServiceHealthPollingService>();

        return services;
    }
}
