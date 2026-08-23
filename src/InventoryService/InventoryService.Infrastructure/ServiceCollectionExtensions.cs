using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using InventoryService.Domain;

namespace InventoryService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved from the service provider's IConfiguration, not the `configuration` parameter
        // captured here: WebApplicationFactory-based tests inject configuration overrides (a real
        // Testcontainers connection string) that only land in the fully-built configuration, not
        // in this pre-Build snapshot — same reasoning as Program.cs's app.Configuration read for
        // Database:RunMigrationsOnStartup.
        services.AddDbContext<InventoryDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
                ?? throw new InvalidOperationException($"Missing configuration section '{InfrastructureOptions.SectionName}'.");
            builder.UseNpgsql(options.ConnectionString);
        });

        services.AddScoped<IInventoryRepository, InventoryRepository>();

        services.Configure<ReservationOptions>(configuration.GetSection(ReservationOptions.SectionName));
        services.AddHostedService<ReservationExpiryService>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }
}
