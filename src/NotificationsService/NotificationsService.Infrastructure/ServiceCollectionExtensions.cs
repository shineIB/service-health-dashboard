using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NotificationsService.Domain;

namespace NotificationsService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(serviceProvider => GetRequiredOptions<RabbitMqOptions>(serviceProvider, RabbitMqOptions.SectionName));

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<INotificationSender, LoggingNotificationSender>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProcessedEventStore, InMemoryProcessedEventStore>();
        services.AddSingleton<OrderEventHandler>();
        services.AddHostedService<OrderEventConsumer>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>(name: "rabbitmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }

    // Every typed options class in this project should be resolved through this helper instead
    // of binding straight off the `IConfiguration configuration` parameter passed into
    // AddNotificationsInfrastructure — that parameter is captured *before*
    // WebApplication.Build(), and WebApplicationFactory-based test overrides only land in the
    // configuration that exists *after* Build(). Binding early silently ignores those overrides.
    // Hit three times in this codebase before becoming a rule instead of a one-off fix — see
    // Directory.Build.props and CLAUDE.md, step 7.
    private static TOptions GetRequiredOptions<TOptions>(IServiceProvider serviceProvider, string sectionName)
        where TOptions : class
    {
        return serviceProvider.GetRequiredService<IConfiguration>()
            .GetSection(sectionName).Get<TOptions>()
            ?? throw new InvalidOperationException($"Missing configuration section '{sectionName}'.");
    }
}
