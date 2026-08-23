using System.Diagnostics;
using Polly.Telemetry;

namespace OrdersService.Infrastructure.Telemetry;

// Polly v8's built-in telemetry emits metrics and log lines out of the box, but no
// OpenTelemetry spans — it only reports through Polly.Telemetry.TelemetryListener. This
// bridges those events (retry attempts, circuit breaker state changes, timeouts) onto the
// "Polly" ActivitySource that Program.cs's OTel pipeline already listens on, so each one
// shows up as its own span nested under the HTTP call it belongs to, not just a log line.
public sealed class PollyActivityTelemetryListener : TelemetryListener
{
    public const string ActivitySourceName = "Polly";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public override void Write<TArgs, TResult>(in TelemetryEventArguments<TArgs, TResult> args)
    {
        using var activity = ActivitySource.StartActivity(args.Event.EventName);
        if (activity is null)
            return;

        activity.SetTag("resilience.pipeline.name", args.Source.PipelineName);
        activity.SetTag("resilience.strategy.name", args.Source.StrategyName);
        activity.SetTag("resilience.event.severity", args.Event.Severity.ToString());

        if (args.Arguments is ExecutionAttemptArguments attempt)
        {
            activity.SetTag("resilience.attempt.number", attempt.AttemptNumber);
            activity.SetTag("resilience.attempt.handled", attempt.Handled);
        }

        if (args.Outcome?.Exception is { } exception)
            activity.SetTag("error.type", exception.GetType().FullName);
    }
}
