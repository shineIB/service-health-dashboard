using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Telemetry;
using OrdersService.Domain;
using OrdersService.Infrastructure.Telemetry;

namespace OrdersService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved from the service provider's IConfiguration, not the `configuration` parameter
        // captured here: WebApplicationFactory-based tests inject configuration overrides (a real
        // Testcontainers connection string) that only land in the fully-built configuration, not
        // in this pre-Build snapshot — same reasoning as Program.cs's app.Configuration read for
        // Database:RunMigrationsOnStartup.
        services.AddDbContext<OrdersDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
                ?? throw new InvalidOperationException($"Missing configuration section '{InfrastructureOptions.SectionName}'.");
            builder.UseNpgsql(options.ConnectionString);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        AddInventoryClient(services, configuration);
        AddEventPublisher(services);

        return services;
    }

    private static void AddEventPublisher(IServiceCollection services)
    {
        // Resolved from the service provider's IConfiguration, not the `configuration` parameter
        // captured here — same reasoning as AddDbContext<OrdersDbContext> above: WebApplicationFactory
        // test overrides only land in the fully-built configuration, not this pre-Build snapshot.
        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                ?? throw new InvalidOperationException($"Missing configuration section '{RabbitMqOptions.SectionName}'."));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
    }

    private static void AddInventoryClient(IServiceCollection services, IConfiguration configuration)
    {
        var inventoryOptions = configuration.GetSection(InventoryClientOptions.SectionName).Get<InventoryClientOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{InventoryClientOptions.SectionName}'.");

        // Applies to every named resilience pipeline in this service, not just this one —
        // there's only one today, but this is where a second one would pick it up for free.
        services.Configure<TelemetryOptions>(options =>
            options.TelemetryListeners.Add(new PollyActivityTelemetryListener()));

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
