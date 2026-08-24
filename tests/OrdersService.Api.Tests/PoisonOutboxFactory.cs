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

// Separate factory (fast poll, low MaxAttempts, a sender that always fails) so
// OutboxDispatcherPoisonMessageTests can observe the give-up-after-MaxAttempts behavior quickly
// and deterministically, without a real RabbitMQ container and without slowing down/affecting
// OutboxDispatcherTests' real-publish scenario — same reasoning as InventoryService's
// ReservationExpiryFactory. Shares the collection's Postgres container via
// PostgresContainerFixture rather than starting its own.
public class PoisonOutboxFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public FakeOutboxSender Sender { get; } = new();
    public FakeInventoryClient InventoryClient { get; } = new() { NextReserveResult = ReserveStockResult.Reserved() };

    public PoisonOutboxFactory(PostgresContainerFixture postgres)
    {
        _connectionString = postgres.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsOnStartup"] = "false",
                ["Infrastructure:ConnectionString"] = _connectionString,
                ["Outbox:PollIntervalSeconds"] = "1",
                ["Outbox:MaxAttempts"] = "3"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOutboxSender>();
            services.AddSingleton<IOutboxSender>(Sender);

            services.RemoveAll<IInventoryClient>();
            services.AddSingleton<IInventoryClient>(InventoryClient);
        });
    }

    public async Task<OutboxMessage?> GetOutboxMessageAsync(Guid orderId)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(_connectionString).Options;
        await using var dbContext = new OrdersDbContext(options);
        return await dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.OrderId == orderId);
    }
}
