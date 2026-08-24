using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OrdersService.Infrastructure.Telemetry;

// Lives in Infrastructure (not Api/Telemetry, unlike OrdersTelemetry) because the publish
// call it instruments happens in RabbitMqEventPublisher — Infrastructure has no reference to
// the Api project, so its own counters/spans have to live here too. Registered into
// Program.cs's WithTracing/WithMetrics the same way PollyActivityTelemetryListener's
// "Polly" ActivitySource already is.
public static class MessagingTelemetry
{
    public const string ActivitySourceName = "OrdersService.Infrastructure.Messaging";
    public const string MeterName = "OrdersService.Infrastructure.Messaging";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> EventsPublished =
        Meter.CreateCounter<long>("orders.events.published");

    public static readonly Counter<long> EventsPublishFailed =
        Meter.CreateCounter<long>("orders.events.publish_failed");
}
