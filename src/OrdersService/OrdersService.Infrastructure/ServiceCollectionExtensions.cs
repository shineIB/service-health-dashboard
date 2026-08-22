using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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

        AddInventoryClient(services, configuration);

        return services;
    }

    private static void AddInventoryClient(IServiceCollection services, IConfiguration configuration)
    {
        var inventoryOptions = configuration.GetSection(InventoryClientOptions.SectionName).Get<InventoryClientOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{InventoryClientOptions.SectionName}'.");

        services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
            {
                client.BaseAddress = new Uri(inventoryOptions.BaseUrl);
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // Retry predicate is left at its default (HttpClientResiliencePredicates.IsTransient):
                // it already retries 5xx/408/429 and network/timeout exceptions, and already excludes
                // 409 (insufficient stock) and 404 (unknown product) — those are answers, not failures.
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);

                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);

                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.MinimumThroughput = 4;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
    }
}
