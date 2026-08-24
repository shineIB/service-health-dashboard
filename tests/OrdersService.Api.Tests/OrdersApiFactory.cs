using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrdersService.Domain;
using OrdersService.Infrastructure;

namespace OrdersService.Api.Tests;

// Connection string comes from PostgresContainerFixture (a real Testcontainers Postgres,
// shared per collection), not from appsettings.json. Startup migrations stay disabled —
// the fixture already applied them once before any test in the collection runs.
// IInventoryClient is still replaced with a fake: no real inventory-service runs for these
// tests, only its own database is real. IOrderEventOutbox is NOT faked — the outbox row landing
// in the same Postgres transaction as the order is the actual thing under test (see
// OutboxTests), so it goes through the real EfOrderEventOutbox against the real container.
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _rabbitMqHostName;
    private readonly int _rabbitMqPort;

    public FakeInventoryClient InventoryClient { get; } = new();

    public OrdersApiFactory(PostgresContainerFixture postgres, RabbitMqContainerFixture rabbitMq)
    {
        _connectionString = postgres.ConnectionString;
        _rabbitMqHostName = rabbitMq.HostName;
        _rabbitMqPort = rabbitMq.Port;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false",
                ["Infrastructure:ConnectionString"] = _connectionString,
                ["RabbitMq:HostName"] = _rabbitMqHostName,
                ["RabbitMq:Port"] = _rabbitMqPort.ToString(),
                // OutboxDispatcher's default 2s poll is fine for a real deployment but makes
                // OutboxDispatcherTests slower than it needs to be — same reasoning as
                // InventoryService's short TTL/sweep interval in ReservationExpiryTests.
                ["Outbox:PollIntervalSeconds"] = "1"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryClient>();
            services.AddSingleton<IInventoryClient>(InventoryClient);
        });
    }

    // Reads the outbox table directly against the same Testcontainers Postgres the app itself
    // uses, via its own short-lived DbContext (not the app's scoped one) — this is what lets a
    // test assert "the row committed in the same transaction as the order" instead of just
    // "some method was called."
    public async Task<List<OutboxMessage>> GetOutboxMessagesAsync(Guid orderId)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(_connectionString).Options;
        await using var dbContext = new OrdersDbContext(options);
        return await dbContext.OutboxMessages
            .Where(m => m.OrderId == orderId)
            .ToListAsync();
    }

    public async Task<int> GetOutboxMessageCountAsync()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(_connectionString).Options;
        await using var dbContext = new OrdersDbContext(options);
        return await dbContext.OutboxMessages.CountAsync();
    }
}
