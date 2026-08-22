using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InventoryService.Infrastructure;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly InventoryDbContext _dbContext;

    public PostgresHealthCheck(InventoryDbContext dbContext)
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
