using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationsService.Domain;
using Xunit;

namespace NotificationsService.Infrastructure.Tests;

// Exercises OrderEventHandler directly with hand-built JSON payloads — this is the wire
// contract orders-service's RabbitMqEventPublisher actually emits (OrderEventPayload), not a
// shared type (see CLAUDE.md, step 7: no shared Contracts assembly between services). No
// RabbitMQ connection involved: HandleAsync only needs a message body and an INotificationSender.
public class OrderEventHandlerTests
{
    private static byte[] ValidPayload(string eventType, Guid? orderId = null, Guid? customerId = null) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OrderId = orderId ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow
        }));

    private static OrderEventHandler CreateHandler(FakeNotificationSender sender) =>
        new(sender, NullLogger<OrderEventHandler>.Instance);

    [Theory]
    [InlineData("order.created", OrderEventType.Created)]
    [InlineData("order.confirmed", OrderEventType.Confirmed)]
    [InlineData("order.cancelled", OrderEventType.Cancelled)]
    public async Task HandleAsync_WithAValidPayload_SendsTheMappedNotificationAndAcks(string eventType, OrderEventType expected)
    {
        var sender = new FakeNotificationSender();
        var handler = CreateHandler(sender);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var acked = await handler.HandleAsync(ValidPayload(eventType, orderId, customerId), CancellationToken.None);

        acked.Should().BeTrue();
        sender.SentNotifications.Should().ContainSingle(n =>
            n.OrderId == orderId && n.CustomerId == customerId && n.EventType == expected);
    }

    [Fact]
    public async Task HandleAsync_WithMalformedJson_DoesNotSendAndNacks()
    {
        var sender = new FakeNotificationSender();
        var handler = CreateHandler(sender);

        var acked = await handler.HandleAsync(Encoding.UTF8.GetBytes("not json"), CancellationToken.None);

        acked.Should().BeFalse();
        sender.SentNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownEventType_DoesNotSendAndNacks()
    {
        var sender = new FakeNotificationSender();
        var handler = CreateHandler(sender);

        var acked = await handler.HandleAsync(ValidPayload("order.shipped"), CancellationToken.None);

        acked.Should().BeFalse();
        sender.SentNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenTheSenderThrows_NacksInsteadOfPropagating()
    {
        var sender = new FakeNotificationSender { NextSendException = new InvalidOperationException("boom") };
        var handler = CreateHandler(sender);

        var acked = await handler.HandleAsync(ValidPayload("order.created"), CancellationToken.None);

        acked.Should().BeFalse();
    }
}
