namespace NotificationsService.Domain;

// Simulates "sending" a confirmation (email/SMS/push) via a structured log line — see
// LoggingNotificationSender. The interface exists so that seam is testable and so a real
// sender can be swapped in later without touching the consumer that calls it.
public interface INotificationSender
{
    Task SendAsync(OrderNotification notification, CancellationToken cancellationToken);
}
