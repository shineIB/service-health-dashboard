using DashboardService.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardService.Infrastructure;

// The API layer only ever reads IServiceHealthCache — it never calls out to a monitored
// service itself. Otherwise every open browser tab polling the dashboard would fan out
// into N requests against every monitored service, so load on them would scale with
// dashboard viewers instead of staying flat. This is the one place that talks to them,
// on its own fixed interval, regardless of how many clients are reading the cache.
public sealed class ServiceHealthPollingService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceHealthCache _cache;
    private readonly MonitoredServicesOptions _monitoredServices;
    private readonly PollingOptions _pollingOptions;
    private readonly ILogger<ServiceHealthPollingService> _logger;

    public ServiceHealthPollingService(
        IHttpClientFactory httpClientFactory,
        IServiceHealthCache cache,
        IOptions<MonitoredServicesOptions> monitoredServices,
        IOptions<PollingOptions> pollingOptions,
        ILogger<ServiceHealthPollingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _monitoredServices = monitoredServices.Value;
        _pollingOptions = pollingOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollingOptions.IntervalSeconds));

        do
        {
            await PollAllAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAllAsync(CancellationToken stoppingToken)
    {
        var checker = new ServiceHealthChecker(_httpClientFactory.CreateClient());
        var timeout = TimeSpan.FromSeconds(_pollingOptions.PerServiceTimeoutSeconds);

        // One task per service, all started together: a slow or unreachable service
        // only delays its own result, never the others'.
        var tasks = _monitoredServices.Services.Select(async service =>
        {
            var previous = _cache.Get(service.Name);
            var snapshot = await checker.CheckAsync(service, previous, timeout, stoppingToken);
            _cache.Set(snapshot);

            if (snapshot.Status != ServiceHealthStatus.Healthy)
            {
                _logger.LogWarning(
                    "{Service} is {Status}: {Detail}",
                    service.Name,
                    snapshot.Status,
                    snapshot.ErrorMessage);
            }
        });

        await Task.WhenAll(tasks);
    }
}
