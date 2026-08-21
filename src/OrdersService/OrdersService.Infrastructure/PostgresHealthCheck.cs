using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrdersService.Infrastructure;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly OrdersDbContext _dbContext;

    public PostgresHealthCheck(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Could not connect to Postgres.");
    }
}
