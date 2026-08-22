using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryService.Infrastructure;

// Reclaims stock from reservations whose TTL has passed. This is what makes TTL a
// real substitute for compensating release calls: even if the caller that created
// a reservation never comes back (crashed, or inventory itself was unreachable when
// it tried to release), the sweep guarantees the stock is freed within one interval
// of the TTL expiring, without depending on any other service being reachable.
public sealed class ReservationExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReservationOptions _options;
    private readonly ILogger<ReservationExpiryService> _logger;

    public ReservationExpiryService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationOptions> options,
        ILogger<ReservationExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.ExpirySweepIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var itemsWithExpiredReservations = await dbContext.Items
            .Include(i => i.Reservations)
            .Where(i => i.Reservations.Any(r => r.ExpiresAtUtc <= nowUtc))
            .ToListAsync(cancellationToken);

        if (itemsWithExpiredReservations.Count == 0)
            return;

        var totalExpired = 0;
        foreach (var item in itemsWithExpiredReservations)
        {
            totalExpired += item.ExpireStaleReservations(nowUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Expired {Count} stale reservation(s) across {ItemCount} product(s).",
            totalExpired,
            itemsWithExpiredReservations.Count);
    }
}
