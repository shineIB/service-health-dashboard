using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationsService.Domain;
using Xunit;

namespace NotificationsService.Infrastructure.Tests;

// Exercises OrderEventHandler directly with hand-built JSON payloads — this is the wire
// contract orders-service's outbox actually emits (OrderEventPayload), not a shared type (see
// CLAUDE.md, step 7: no shared Contracts assembly between services). No RabbitMQ connection
// involved: HandleAsync only needs a message body, an INotificationSender, and an
// IProcessedEventStore — the real InMemoryProcessedEventStore is used as-is (no I/O, no fake
// needed) so duplicate-detection tests exercise the real dedupe logic, not a stand-in for it.
public class OrderEventHandlerTests
{
    private static byte[] ValidPayload(string eventType, Guid? eventId = null, Guid? orderId = null, Guid? customerId = null) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            EventId = eventId ?? Guid.NewGuid(),
            EventType = eventType,
            OrderId = orderId ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow
        }));

    private static OrderEventHandler CreateHandler(FakeNotificationSender sender) =>
        new(sender, new InMemoryProcessedEventStore(TimeProvider.System), NullLogger<OrderEventHandler>.Instance);

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

        var acked = await handler.HandleAsync(ValidPayload(eventType, orderId: orderId, customerId: customerId), CancellationToken.None);

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

    [Fact]
    public async Task HandleAsync_WithARedeliveredEventId_AcksWithoutSendingASecondTime()
    {
        var sender = new FakeNotificationSender();
        var handler = CreateHandler(sender);
        var eventId = Guid.NewGuid();
        var payload = ValidPayload("order.created", eventId: eventId);

        var firstAck = await handler.HandleAsync(payload, CancellationToken.None);
        var secondAck = await handler.HandleAsync(payload, CancellationToken.None);

        firstAck.Should().BeTrue();
        // The redelivery is acked too, not nacked — it's a duplicate, not a failure, and
        // nacking it would just cause RabbitMQ to redeliver it again forever.
        secondAck.Should().BeTrue();
        sender.SentNotifications.Should().HaveCount(1, "a redelivered event must not be acted on twice");
    }
}
