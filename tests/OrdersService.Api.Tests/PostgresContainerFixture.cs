using Microsoft.EntityFrameworkCore;
using OrdersService.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrdersService.Api.Tests;

// Registered once per collection (see OrdersApiCollection) so every WebApplicationFactory
// in the collection shares the same running container instead of each test class paying its
// own Testcontainers startup cost. Applies migrations itself, once, before any test runs —
// the factory that uses this connection string keeps startup migrations disabled.
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("orders")
        .WithUsername("orders")
        .WithPassword("orders")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var dbContext = new OrdersDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
