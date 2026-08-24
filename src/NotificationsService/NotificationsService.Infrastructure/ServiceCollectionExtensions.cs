using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationsService.Domain;

namespace NotificationsService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved from the service provider's IConfiguration, not the `configuration` parameter
        // (captured pre-Build) — WebApplicationFactory test overrides (a real Testcontainers
        // RabbitMQ host/port) only land in the fully-built configuration. Same trap CLAUDE.md
        // already documents for OrdersDbContext's connection string.
        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                ?? throw new InvalidOperationException($"Missing configuration section '{RabbitMqOptions.SectionName}'."));

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<INotificationSender, LoggingNotificationSender>();
        services.AddSingleton<OrderEventHandler>();
        services.AddHostedService<OrderEventConsumer>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>(name: "rabbitmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }
}
