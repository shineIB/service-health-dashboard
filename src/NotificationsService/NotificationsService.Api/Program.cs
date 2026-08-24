using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using NotificationsService.Api.Configuration;
using NotificationsService.Api.Endpoints;
using NotificationsService.Infrastructure;
using NotificationsService.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// JSON console only — no Serilog, one dependency fewer. IncludeScopes is what makes the
// trace_id/span_id scope below (see app.Use below) actually show up in the output.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
// Off: the framework's own PascalCase TraceId/SpanId scope would otherwise duplicate the
// trace_id/span_id scope added below.
builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);

builder.Services.AddOpenApi();
builder.Services.AddNotificationsInfrastructure(builder.Configuration);

builder.Services.Configure<BuildInfoOptions>(builder.Configuration.GetSection(BuildInfoOptions.SectionName));

builder.Services.AddOpenTelemetry()
    // Without this, every service reports as "unknown_service:dotnet" in Jaeger — the SDK
    // doesn't infer a useful name from the entry assembly on its own.
    .ConfigureResource(resource => resource.AddService("notifications-service"))
    .WithTracing(tracing => tracing
        .SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // notifications.handle-event spans around each consumed message — see OrderEventHandler.
        .AddSource(NotificationsTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // notifications.sent / notifications.failed — see NotificationsTelemetry.
        .AddMeter(NotificationsTelemetry.MeterName)
        // Pull-based, not OTLP push: Jaeger only understands traces, and Prometheus scrapes
        // this endpoint directly — see k8s/prometheus.
        .AddPrometheusExporter());

var app = builder.Build();

// Enriches every log line written during this request with trace_id/span_id so a log can be
// followed to its trace in Jaeger. Activity.Current is already populated by ASP.NET Core's
// own request activity at this point, before any instrumentation-specific middleware runs.
app.Use(async (context, next) =>
{
    var activity = Activity.Current;
    if (activity is null)
    {
        await next(context);
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TraceCorrelation");
    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["trace_id"] = activity.TraceId.ToString(),
        ["span_id"] = activity.SpanId.ToString()
    }))
    {
        await next(context);
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapVersionEndpoint();
app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// RabbitMq is a hard dependency here (unlike orders-service): this service's only job is
// consuming from it, via RabbitMqHealthCheck (tagged "ready").
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;
