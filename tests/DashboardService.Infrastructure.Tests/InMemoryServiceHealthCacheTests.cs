using FluentAssertions;
using DashboardService.Domain;
using Xunit;

namespace DashboardService.Infrastructure.Tests;

public class InMemoryServiceHealthCacheTests
{
    private static ServiceHealthSnapshot Snapshot(string name, ServiceHealthStatus status = ServiceHealthStatus.Healthy) =>
        new(name, $"http://{name}", status, null, null, null, null, null, null);

    [Fact]
    public void Set_ThenGet_ReturnsTheStoredSnapshot()
    {
        var cache = new InMemoryServiceHealthCache();
        var snapshot = Snapshot("orders-service");

        cache.Set(snapshot);

        cache.Get("orders-service").Should().Be(snapshot);
    }

    [Fact]
    public void Get_ForUnknownService_ReturnsNull()
    {
        var cache = new InMemoryServiceHealthCache();

        cache.Get("unknown-service").Should().BeNull();
    }

    [Fact]
    public void Set_CalledAgainForTheSameService_ReplacesThePreviousSnapshot()
    {
        var cache = new InMemoryServiceHealthCache();
        cache.Set(Snapshot("orders-service", ServiceHealthStatus.Healthy));

        cache.Set(Snapshot("orders-service", ServiceHealthStatus.Unreachable));

        cache.Get("orders-service")!.Status.Should().Be(ServiceHealthStatus.Unreachable);
    }

    [Fact]
    public void GetAll_ReturnsAllStoredSnapshotsOrderedByServiceName()
    {
        var cache = new InMemoryServiceHealthCache();
        cache.Set(Snapshot("orders-service"));
        cache.Set(Snapshot("inventory-service"));

        var all = cache.GetAll();

        all.Select(s => s.ServiceName).Should().Equal("inventory-service", "orders-service");
    }
}
