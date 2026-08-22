using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InventoryService.Domain;

namespace InventoryService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{InfrastructureOptions.SectionName}'.");

        services.AddDbContext<InventoryDbContext>(builder => builder.UseNpgsql(options.ConnectionString));

        services.AddScoped<IInventoryRepository, InventoryRepository>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }
}
