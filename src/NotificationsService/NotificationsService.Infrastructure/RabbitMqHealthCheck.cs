using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NotificationsService.Infrastructure;

// Unlike orders-service (where RabbitMQ is best-effort and never affects /health/ready),
// RabbitMQ is a hard dependency here — this service's only job is consuming from it. Reads
// RabbitMqConnectionProvider's last-known state rather than probing fresh: a real probe would
// itself need a connection attempt, duplicating what OrderEventConsumer's retry loop already
// does.
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnectionProvider _connectionProvider;

    public RabbitMqHealthCheck(RabbitMqConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = _connectionProvider.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Not connected to RabbitMQ.");
        return Task.FromResult(result);
    }
}
