using System.Net;
using FluentAssertions;
using Xunit;

namespace NotificationsService.Api.Tests;

[Collection(NotificationsApiCollection.Name)]
public class HealthEndpointTests : IClassFixture<NotificationsApiFactory>
{
    private readonly NotificationsApiFactory _factory;

    public HealthEndpointTests(NotificationsApiFactory factory)
    {
        _factory = factory;
    }

    // OrderEventConsumer connects asynchronously in the background after the host starts, so
    // readiness may briefly be Unhealthy right after the factory boots — same reasoning as
    // MetricsEndpointTests.ScrapeMetricsUntilAsync in the other test projects: poll for a
    // background condition instead of asserting against a single race-prone read.
    private static async Task<HttpStatusCode> PollReadyUntilAsync(HttpClient client, HttpStatusCode expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var last = HttpStatusCode.ServiceUnavailable;
        while (DateTime.UtcNow < deadline)
        {
            last = (await client.GetAsync("/health/ready")).StatusCode;
            if (last == expected)
                break;

            await Task.Delay(100);
        }

        return last;
    }

    [Fact]
    public async Task Live_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_ReturnsHealthy_OnceConnectedToRabbitMq()
    {
        var client = _factory.CreateClient();

        var status = await PollReadyUntilAsync(client, HttpStatusCode.OK);

        status.Should().Be(HttpStatusCode.OK, "RabbitMQ is a hard dependency here — readiness must reflect a real connection");
    }

    [Fact]
    public async Task Ready_ReturnsUnhealthy_WhenRabbitMqIsUnreachable()
    {
        using var factory = new UnreachableNotificationsApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
