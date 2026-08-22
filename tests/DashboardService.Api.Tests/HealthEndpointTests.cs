using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DashboardService.Domain;
using Xunit;

namespace DashboardService.Api.Tests;

public class HealthEndpointTests : IClassFixture<DashboardApiFactory>
{
    private readonly DashboardApiFactory _factory;

    public HealthEndpointTests(DashboardApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_ReturnsHealthy_EvenWhenEveryMonitoredServiceIsDown()
    {
        // This is the requirement from CLAUDE.md: dashboard-api is healthy as long as it
        // can report, even while reporting that everything else is on fire. Seed the cache
        // with the worst case for every monitored service and confirm readiness doesn't move.
        var cache = _factory.Services.GetRequiredService<IServiceHealthCache>();
        cache.Set(new ServiceHealthSnapshot(
            "orders-service", "http://orders-service:8080", ServiceHealthStatus.Unreachable,
            null, null, null, null, null, "connection refused"));
        cache.Set(new ServiceHealthSnapshot(
            "inventory-service", "http://inventory-service:8080", ServiceHealthStatus.Unhealthy,
            "1.0.0", "abc123", "2026-01-01T00:00:00Z", 500, DateTimeOffset.UtcNow, "Responded with HTTP 503."));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "dashboard-api's own readiness must never reflect a monitored service being down");
    }
}
