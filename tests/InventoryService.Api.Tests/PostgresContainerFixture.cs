using Microsoft.EntityFrameworkCore;
using InventoryService.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace InventoryService.Api.Tests;

// Registered once per collection (see InventoryApiCollection) so every WebApplicationFactory
// in the collection shares the same running container instead of each test class paying its
// own Testcontainers startup cost. Applies migrations itself, once, before any test runs —
// the factories that use this connection string keep startup migrations disabled.
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("inventory")
        .WithUsername("inventory")
        .WithPassword("inventory")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
