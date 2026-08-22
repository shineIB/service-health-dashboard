using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DashboardService.Api.Contracts;
using DashboardService.Domain;
using Xunit;

namespace DashboardService.Api.Tests;

public class DashboardEndpointsTests : IClassFixture<DashboardApiFactory>
{
    private readonly DashboardApiFactory _factory;

    public DashboardEndpointsTests(DashboardApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetServices_ReturnsSnapshotsFromTheCache()
    {
        var cache = _factory.Services.GetRequiredService<IServiceHealthCache>();
        var checkedAt = DateTimeOffset.UtcNow;
        cache.Set(new ServiceHealthSnapshot(
            "orders-service", "http://orders-service:8080", ServiceHealthStatus.Healthy,
            "1.0.0", "abc123", "2026-01-01T00:00:00Z", 12, checkedAt, null));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/services");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<ServiceStatusResponse>>();
        body.Should().ContainSingle(s =>
            s.ServiceName == "orders-service" &&
            s.Status == "Healthy" &&
            s.Version == "1.0.0" &&
            s.GitSha == "abc123" &&
            s.ResponseTimeMs == 12);
    }

    [Fact]
    public async Task GetServices_ForAnUnreachableService_ReportsUnreachableWithNoResponseTime()
    {
        var cache = _factory.Services.GetRequiredService<IServiceHealthCache>();
        cache.Set(new ServiceHealthSnapshot(
            "inventory-service", "http://inventory-service:8080", ServiceHealthStatus.Unreachable,
            null, null, null, null, null, "connection refused"));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/services");

        var body = await response.Content.ReadFromJsonAsync<List<ServiceStatusResponse>>();
        body.Should().ContainSingle(s =>
            s.ServiceName == "inventory-service" &&
            s.Status == "Unreachable" &&
            s.ResponseTimeMs == null);
    }
}
