using System.Net;
using System.Text.Json;
using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace NotificationsService.Api.Tests;

// Presence checks only, never exact counter values — see the other services'
// MetricsEndpointTests for why (NotificationsTelemetry's Meter is process-wide static state).
[Collection(NotificationsApiCollection.Name)]
public class MetricsEndpointTests : IClassFixture<NotificationsApiFactory>
{
    private readonly NotificationsApiFactory _factory;
    private readonly RabbitMqContainerFixture _rabbitMq;

    public MetricsEndpointTests(NotificationsApiFactory factory, RabbitMqContainerFixture rabbitMq)
    {
        _factory = factory;
        _rabbitMq = rabbitMq;
    }

    private static async Task<string> ScrapeMetricsUntilAsync(HttpClient client, string expectedSubstring)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var last = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            last = await (await client.GetAsync("/metrics")).Content.ReadAsStringAsync();
            if (last.Contains(expectedSubstring, StringComparison.Ordinal))
                break;

            await Task.Delay(100);
        }

        return last;
    }

    // Publishes directly onto the real container's "orders" exchange the same way
    // orders-service's RabbitMqEventPublisher does — this is the wire contract, not a shared
    // type (see CLAUDE.md, step 7). Declares the exchange idempotently first: the app's own
    // OrderEventConsumer normally does this at startup, but a test shouldn't rely on winning
    // that race.
    private async Task PublishOrderCreatedEventAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMq.HostName,
            Port = _rabbitMq.Port,
            UserName = "guest",
            Password = "guest",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false);

        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            EventId = Guid.NewGuid(),
            EventType = "order.created",
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        await channel.BasicPublishAsync("orders", "order.created", body);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusTextExposingAspNetCoreInstrumentation()
    {
        var client = _factory.CreateClient();
        await client.GetAsync("/version");

        // Same race as the other services' MetricsEndpointTests (see their comment): a request's
        // http_server_request_duration measurement isn't always visible in the very next
        // scrape — observed for real on GitHub Actions' runners (not just a theoretical race),
        // where a one-shot scrape right after /version flaked twice in a row. Poll instead of
        // asserting against a single read.
        var body = await ScrapeMetricsUntilAsync(client, "http_server_request_duration");

        body.Should().Contain("http_server_request_duration");
    }

    [Fact]
    public async Task Metrics_AfterConsumingAnOrderCreatedEvent_ExposesNotificationsSentCounter()
    {
        var client = _factory.CreateClient();

        // OrderEventConsumer's connection becomes IsOpen (readiness) before it finishes
        // declaring and binding the queue — publishing right after "ready" can still land on
        // an exchange with no binding yet (a topic exchange silently drops an unroutable
        // message rather than queuing it). Re-publishing on every poll iteration, instead of
        // once up front, means the message eventually lands after the binding completes
        // without hard-coding how long that takes.
        var metrics = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await PublishOrderCreatedEventAsync();
            metrics = await (await client.GetAsync("/metrics")).Content.ReadAsStringAsync();
            if (metrics.Contains("notifications_sent_total", StringComparison.Ordinal))
                break;

            await Task.Delay(250);
        }

        metrics.Should().Contain("notifications_sent_total");
    }
}
