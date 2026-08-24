using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrdersService.Api.Contracts;
using OrdersService.Domain;
using OrdersService.Infrastructure;
using Xunit;

namespace OrdersService.Api.Tests;

// Proves the fix for CLAUDE.md, step 7.6: a message that can never publish must stop
// consuming a batch slot after OutboxOptions.MaxAttempts, instead of retrying forever. Uses
// FakeOutboxSender (always throws) via PoisonOutboxFactory — no real RabbitMQ needed, this is
// about OutboxDispatcher's own give-up logic, not the publish call itself.
[Collection(OrdersApiCollection.Name)]
public class OutboxDispatcherPoisonMessageTests : IClassFixture<PoisonOutboxFactory>
{
    private readonly PoisonOutboxFactory _factory;

    public OutboxDispatcherPoisonMessageTests(PoisonOutboxFactory factory)
    {
        _factory = factory;
    }

    private static object ValidCreateOrderRequest() => new
    {
        customerId = Guid.NewGuid(),
        items = new[] { new { productId = Guid.NewGuid(), quantity = 1, unitPrice = 5m } }
    };

    [Fact]
    public async Task AMessageThatAlwaysFails_IsMarkedFailedAfterMaxAttemptsAndStopsBeingRetried()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();

        // MaxAttempts=3, PollIntervalSeconds=1 (see PoisonOutboxFactory) — comfortably done well
        // within this deadline even accounting for poll-timing slack.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        OutboxMessage? message = null;
        while (DateTime.UtcNow < deadline)
        {
            message = await _factory.GetOutboxMessageAsync(order!.Id);
            if (message?.FailedAtUtc is not null)
                break;

            await Task.Delay(200);
        }

        message.Should().NotBeNull();
        message!.FailedAtUtc.Should().NotBeNull("the dispatcher must give up once MaxAttempts is reached");
        message.Attempts.Should().Be(3);
        message.LastError.Should().Contain("Simulated permanent publish failure");
        message.PublishedAtUtc.Should().BeNull();

        // Confirms the row is actually excluded from further attempts, not just flagged while
        // the dispatcher keeps hammering it: the sender's call count should stop growing once
        // FailedAtUtc is set — wait one more poll interval and check nothing changed.
        var callCountAtGiveUp = _factory.Sender.CallCount;
        await Task.Delay(TimeSpan.FromSeconds(2));
        _factory.Sender.CallCount.Should().Be(callCountAtGiveUp, "a failed row must not be retried after giving up");
    }

    [Fact]
    public async Task APoisonMessage_DoesNotPreventAHealthyOneInTheSameBatchFromBeingMarkedFailedIndependently()
    {
        // Both orders in this test use the same always-failing FakeOutboxSender, so "healthy"
        // here means "processed independently" (its own Attempts/FailedAtUtc), not
        // "successfully published" — proving the per-row loop doesn't stop or share state across
        // rows in a batch. OutboxDispatcherTests (real RabbitMQ) already proves a genuinely
        // healthy row publishes successfully end to end.
        var client = _factory.CreateClient();

        var firstOrder = await (await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest()))
            .Content.ReadFromJsonAsync<OrderResponse>();
        var secondOrder = await (await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest()))
            .Content.ReadFromJsonAsync<OrderResponse>();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        OutboxMessage? firstMessage = null;
        OutboxMessage? secondMessage = null;
        while (DateTime.UtcNow < deadline)
        {
            firstMessage = await _factory.GetOutboxMessageAsync(firstOrder!.Id);
            secondMessage = await _factory.GetOutboxMessageAsync(secondOrder!.Id);
            if (firstMessage?.FailedAtUtc is not null && secondMessage?.FailedAtUtc is not null)
                break;

            await Task.Delay(200);
        }

        firstMessage!.FailedAtUtc.Should().NotBeNull();
        secondMessage!.FailedAtUtc.Should().NotBeNull("a failing row earlier in the batch must not stop a later row from being attempted and given up on independently");
    }
}
