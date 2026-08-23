using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrdersService.Api;
using OrdersService.Api.Configuration;
using OrdersService.Api.Endpoints;
using OrdersService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// JSON console only — no Serilog, one dependency fewer. IncludeScopes is what makes the
// trace_id/span_id scope below (see app.Use below) actually show up in the output.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
// Off: the framework's own PascalCase TraceId/SpanId scope would otherwise duplicate the
// trace_id/span_id scope added below.
builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);

builder.Services.AddOpenApi();
builder.Services.AddOrdersInfrastructure(builder.Configuration);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<BuildInfoOptions>(builder.Configuration.GetSection(BuildInfoOptions.SectionName));

builder.Services.AddOpenTelemetry()
    // Without this, every service reports as "unknown_service:dotnet" in Jaeger — the SDK
    // doesn't infer a useful name from the entry assembly on its own.
    .ConfigureResource(resource => resource.AddService("orders-service"))
    .WithTracing(tracing => tracing
        // 100% locally — a real deployment needs a sampling strategy (head-based ratio or
        // tail-based in a collector); see CLAUDE.md.
        .SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Npgsql emits its own spans (ActivitySource "Npgsql") without a separate
        // instrumentation package — just needs a listener.
        .AddSource("Npgsql")
        // Polly v8's resilience telemetry (ActivitySource "Polly") — this is what turns
        // each retry attempt and the circuit breaker opening into their own spans.
        .AddSource("Polly")
        // Defaults to http://localhost:4317; overridden via OTEL_EXPORTER_OTLP_ENDPOINT
        // (see k8s/jaeger and docker-compose.yml) — the OTel SDK reads that env var itself.
        .AddOtlpExporter());

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

// Read via app.Configuration (post-Build), not builder.Configuration: WebApplicationFactory-based
// tests inject configuration overrides that only land in the fully-built configuration.
var databaseOptions = app.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
    ?? new DatabaseOptions();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGroup("/orders").MapOrderEndpoints();
app.MapVersionEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

if (databaseOptions.RunMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Lets a Kubernetes Job run "dotnet OrdersService.Api.dll --migrate-only" to apply the
// migration and exit 0, instead of starting Kestrel and blocking forever — a Job's pod
// must terminate on its own for the Job to be considered complete.
if (args.Contains("--migrate-only"))
{
    return;
}

app.Run();

public partial class Program;
