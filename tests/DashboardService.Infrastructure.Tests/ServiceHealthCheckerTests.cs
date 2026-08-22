using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using DashboardService.Domain;
using Xunit;

namespace DashboardService.Infrastructure.Tests;

public class ServiceHealthCheckerTests
{
    private static readonly MonitoredServiceEntry Service = new() { Name = "orders-service", BaseUrl = "http://orders-service" };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task CheckAsync_WhenHealthyAndVersionSucceeds_ReturnsHealthyWithVersionInfo()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/health/ready")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                new { version = "1.2.3", gitSha = "abc123", buildTimeUtc = "2026-01-01T00:00:00Z" }));
        });
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var snapshot = await checker.CheckAsync(Service, previous: null, TimeSpan.FromSeconds(2), CancellationToken.None);

        snapshot.Status.Should().Be(ServiceHealthStatus.Healthy);
        snapshot.Version.Should().Be("1.2.3");
        snapshot.GitSha.Should().Be("abc123");
        snapshot.BuildTimeUtc.Should().Be("2026-01-01T00:00:00Z");
        snapshot.ResponseTimeMs.Should().NotBeNull();
        snapshot.LastSuccessfulCheckUtc.Should().NotBeNull();
        snapshot.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenHealthEndpointRespondsWithFailureStatus_ReturnsUnhealthy()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var snapshot = await checker.CheckAsync(Service, previous: null, TimeSpan.FromSeconds(2), CancellationToken.None);

        snapshot.Status.Should().Be(ServiceHealthStatus.Unhealthy);
        snapshot.ErrorMessage.Should().Contain("503");
        snapshot.LastSuccessfulCheckUtc.Should().NotBeNull("the service did respond, just not successfully");
    }

    [Fact]
    public async Task CheckAsync_WhenHealthySucceedsButVersionCallFails_StillReturnsHealthy()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/health/ready")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            throw new HttpRequestException("version endpoint down");
        });
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var snapshot = await checker.CheckAsync(Service, previous: null, TimeSpan.FromSeconds(2), CancellationToken.None);

        snapshot.Status.Should().Be(ServiceHealthStatus.Healthy, "a failed /version call is not a health failure");
        snapshot.Version.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenNoResponseAtAll_ReturnsUnreachable()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var snapshot = await checker.CheckAsync(Service, previous: null, TimeSpan.FromSeconds(2), CancellationToken.None);

        snapshot.Status.Should().Be(ServiceHealthStatus.Unreachable);
        snapshot.ResponseTimeMs.Should().BeNull();
        snapshot.LastSuccessfulCheckUtc.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenServiceIsSlowerThanTheTimeout_ReturnsUnreachableWithinTheTimeoutBudget()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var stopwatch = Stopwatch.StartNew();
        var snapshot = await checker.CheckAsync(Service, previous: null, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        stopwatch.Stop();

        snapshot.Status.Should().Be(ServiceHealthStatus.Unreachable);
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "a per-service timeout must bound how long one slow service can take, so it never delays the others");
    }

    [Fact]
    public async Task CheckAsync_WhenUnreachable_PreservesVersionAndLastSuccessfulCheckFromThePreviousSnapshot()
    {
        var previous = new ServiceHealthSnapshot(
            "orders-service",
            "http://orders-service",
            ServiceHealthStatus.Healthy,
            "1.0.0",
            "deadbeef",
            "2026-01-01T00:00:00Z",
            42,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null);
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var checker = new ServiceHealthChecker(new HttpClient(handler));

        var snapshot = await checker.CheckAsync(Service, previous, TimeSpan.FromSeconds(2), CancellationToken.None);

        snapshot.Status.Should().Be(ServiceHealthStatus.Unreachable);
        snapshot.Version.Should().Be("1.0.0");
        snapshot.GitSha.Should().Be("deadbeef");
        snapshot.LastSuccessfulCheckUtc.Should().Be(previous.LastSuccessfulCheckUtc,
            "a service going dark shouldn't erase what we last knew about it");
    }
}
