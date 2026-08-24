using Microsoft.Extensions.Logging;
using NotificationsService.Domain;

namespace NotificationsService.Infrastructure;

// Simulates sending a confirmation — CLAUDE.md's "loggar/'skickar' bekräftelser". A real
// sender (email/SMS/push provider) would implement INotificationSender the same way and
// slot in here without touching OrderEventConsumer/OrderEventHandler.
public sealed class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _logger;

    public LoggingNotificationSender(ILogger<LoggingNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Sending {EventType} confirmation to customer {CustomerId} for order {OrderId}.",
            notification.EventType,
            notification.CustomerId,
            notification.OrderId);
        return Task.CompletedTask;
    }
}
