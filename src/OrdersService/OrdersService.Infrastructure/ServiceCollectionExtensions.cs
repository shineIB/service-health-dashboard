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
        // in this pre-Build snapshot — see GetRequiredOptions below, and Directory.Build.props.
        services.AddDbContext<OrdersDbContext>((serviceProvider, builder) =>
        {
            var options = GetRequiredOptions<InfrastructureOptions>(serviceProvider, InfrastructureOptions.SectionName);
            builder.UseNpgsql(options.ConnectionString);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        AddInventoryClient(services);
        AddEventPublisher(services);

        return services;
    }

    // Every typed options class in this project should be resolved through this helper (or the
    // equivalent (serviceProvider, x) => ... factory pattern for things that aren't plain
    // singletons, like AddDbContext/AddHttpClient above/below) instead of binding straight off
    // the `IConfiguration configuration` parameter passed into AddOrdersInfrastructure — that
    // parameter is captured *before* WebApplication.Build(), and WebApplicationFactory-based test
    // overrides only land in the configuration that exists *after* Build(). Binding early silently
    // ignores those overrides. Hit three times in this codebase before becoming a rule instead of
    // a one-off fix — see Directory.Build.props and CLAUDE.md, step 7.
    private static TOptions GetRequiredOptions<TOptions>(IServiceProvider serviceProvider, string sectionName)
        where TOptions : class
    {
        return serviceProvider.GetRequiredService<IConfiguration>()
            .GetSection(sectionName).Get<TOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{sectionName}'.");
    }

    private static void AddEventPublisher(IServiceCollection services)
    {
        services.AddSingleton(serviceProvider => GetRequiredOptions<RabbitMqOptions>(serviceProvider, RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
    }

    private static void AddInventoryClient(IServiceCollection services)
    {
        // Applies to every named resilience pipeline in this service, not just this one —
        // there's only one today, but this is where a second one would pick it up for free.
        services.Configure<TelemetryOptions>(options =>
            options.TelemetryListeners.Add(new PollyActivityTelemetryListener()));

        services.AddHttpClient<IInventoryClient, InventoryClient>((serviceProvider, client) =>
            {
                var options = GetRequiredOptions<InventoryClientOptions>(serviceProvider, InventoryClientOptions.SectionName);
                client.BaseAddress = new Uri(options.BaseUrl);
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
