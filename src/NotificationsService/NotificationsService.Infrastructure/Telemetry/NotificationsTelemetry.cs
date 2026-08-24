using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NotificationsService.Infrastructure.Telemetry;

// Lives in Infrastructure, not Api/Telemetry: the span/counters it exposes are used from
// OrderEventHandler, which has no reference back to the Api project — same reasoning as
// orders-service's MessagingTelemetry.
public static class NotificationsTelemetry
{
    public const string ActivitySourceName = "NotificationsService.Infrastructure";
    public const string MeterName = "NotificationsService.Infrastructure";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> NotificationsSent =
        Meter.CreateCounter<long>("notifications.sent");

    public static readonly Counter<long> NotificationsFailed =
        Meter.CreateCounter<long>("notifications.failed");
}
