using NotificationsService.Domain;

namespace NotificationsService.Infrastructure.Tests;

public sealed class FakeNotificationSender : INotificationSender
{
    public List<OrderNotification> SentNotifications { get; } = [];
    public Exception? NextSendException { get; set; }

    public Task SendAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (NextSendException is not null)
            throw NextSendException;

        SentNotifications.Add(notification);
        return Task.CompletedTask;
    }
}
