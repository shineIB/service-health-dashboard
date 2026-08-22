using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using DashboardService.Api.Configuration;
using DashboardService.Api.Endpoints;
using DashboardService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDashboardInfrastructure(builder.Configuration);

builder.Services.Configure<BuildInfoOptions>(builder.Configuration.GetSection(BuildInfoOptions.SectionName));

// No checks added: dashboard-api has no database and no dependency of its own to check.
// This is what actually guarantees it can never report Unhealthy because a *monitored*
// service is down — there is nothing in this health-check pipeline that looks at
// IServiceHealthCache. Readiness here means only "can this process serve responses."
// See DashboardHealthTests for the test that locks this in.
builder.Services.AddHealthChecks();

var app = builder.Build();

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
