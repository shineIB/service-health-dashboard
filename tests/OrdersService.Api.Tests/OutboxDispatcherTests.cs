using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrdersService.Api.Contracts;
using OrdersService.Domain;
using Xunit;

namespace OrdersService.Api.Tests;

// Exercises OutboxDispatcher for real against the collection's RabbitMqContainerFixture — the
// point is proving the row created in the same Postgres transaction as the order actually gets
// delivered to a real broker, not just that OrderEndpoints wrote a row (OrderEndpointsTests
// already covers that in isolation). See CLAUDE.md, step 7.5.
[Collection(OrdersApiCollection.Name)]
public class OutboxDispatcherTests : IClassFixture<OrdersApiFactory>
{
    private readonly OrdersApiFactory _factory;

    public OutboxDispatcherTests(OrdersApiFactory factory)
    {
        _factory = factory;
        _factory.InventoryClient.NextReserveResult = ReserveStockResult.Reserved();
    }

    private static object ValidCreateOrderRequest() => new
    {
        customerId = Guid.NewGuid(),
        items = new[] { new { productId = Guid.NewGuid(), quantity = 1, unitPrice = 5m } }
    };

    private async Task<Guid> WaitForPublishedAsync(Guid orderId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var messages = await _factory.GetOutboxMessagesAsync(orderId);
            var published = messages.FirstOrDefault(m => m.PublishedAtUtc != null);
            if (published is not null)
                return published.Id;

            await Task.Delay(200);
        }

        throw new TimeoutException($"No outbox message for order {orderId} was published within {timeout}.");
    }

    [Fact]
    public async Task CreatedOrder_OutboxRowIsPublishedByTheDispatcherWithoutAnyExplicitPublishCall()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/orders", ValidCreateOrderRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();

        // No call into IEventPublisher/RabbitMqOutboxSender from this test at all — the row
        // OrderEndpoints wrote is picked up by the real, unmodified OutboxDispatcher background
        // service running inside the host, exactly as it would in production.
        await WaitForPublishedAsync(order!.Id, TimeSpan.FromSeconds(5));

        var messages = await _factory.GetOutboxMessagesAsync(order.Id);
        messages.Should().ContainSingle(m => m.EventType == "order.created");
        messages[0].PublishedAtUtc.Should().NotBeNull();
        messages[0].LastError.Should().BeNull();
    }
}
