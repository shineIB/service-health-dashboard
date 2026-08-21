using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrdersService.Domain;

namespace OrdersService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{InfrastructureOptions.SectionName}'.");

        services.AddDbContext<OrdersDbContext>(builder => builder.UseNpgsql(options.ConnectionString));

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }
}
