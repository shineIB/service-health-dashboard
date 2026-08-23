using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using DashboardService.Api.Configuration;
using DashboardService.Api.Endpoints;
using DashboardService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// JSON console only — no Serilog, one dependency fewer. IncludeScopes is what makes the
// trace_id/span_id scope below (see app.Use below) actually show up in the output.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
// Off: the framework's own PascalCase TraceId/SpanId scope would otherwise duplicate the
// trace_id/span_id scope added below.
builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);

builder.Services.AddOpenApi();
builder.Services.AddDashboardInfrastructure(builder.Configuration);

builder.Services.Configure<BuildInfoOptions>(builder.Configuration.GetSection(BuildInfoOptions.SectionName));

// No checks added: dashboard-api has no database and no dependency of its own to check.
// This is what actually guarantees it can never report Unhealthy because a *monitored*
// service is down — there is nothing in this health-check pipeline that looks at
// IServiceHealthCache. Readiness here means only "can this process serve responses."
// See DashboardHealthTests for the test that locks this in.
builder.Services.AddHealthChecks();

// dashboard-service has no database and calls no other service resiliently (no Polly), so
// it only needs the two general-purpose instrumentations — but its own /health/ready polling
// calls to orders-service/inventory-service still show up as HTTP client spans, same as any
// other outgoing call.
builder.Services.AddOpenTelemetry()
    // Without this, every service reports as "unknown_service:dotnet" in Jaeger — the SDK
    // doesn't infer a useful name from the entry assembly on its own.
    .ConfigureResource(resource => resource.AddService("dashboard-service"))
    .WithTracing(tracing => tracing
        .SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    // No custom Meter here (unlike orders/inventory) — dashboard-service has no domain
    // outcomes of its own to count yet; ServiceHealthPollingService's own success/failure
    // rate is a natural next metric, not added now to keep this step to auto-instrumentation.
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Pull-based, not OTLP push: Jaeger only understands traces, and there's no
        // Prometheus/Grafana deployed yet to receive a push either (see CLAUDE.md, step 6
        // part 2). Scraped at GET /metrics until that infra exists.
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

// Serves the built React SPA (see DashboardService.Web) from wwwroot — the Dockerfile
// copies the Vite build output there. Same origin as the API, so no CORS and no extra pod.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDashboardEndpoints();
app.MapVersionEndpoint();
app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
