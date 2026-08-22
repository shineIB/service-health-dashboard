using System.Collections.Concurrent;
using DashboardService.Domain;

namespace DashboardService.Infrastructure;

// One writer (ServiceHealthPollingService), many readers (concurrent API requests) —
// ConcurrentDictionary needs nothing extra for that; no locking, no seqlock, no drama.
public sealed class InMemoryServiceHealthCache : IServiceHealthCache
{
    private readonly ConcurrentDictionary<string, ServiceHealthSnapshot> _snapshots = new();

    public IReadOnlyList<ServiceHealthSnapshot> GetAll() =>
        _snapshots.Values.OrderBy(s => s.ServiceName, StringComparer.Ordinal).ToList();

    public ServiceHealthSnapshot? Get(string serviceName) =>
        _snapshots.GetValueOrDefault(serviceName);

    public void Set(ServiceHealthSnapshot snapshot) =>
        _snapshots[snapshot.ServiceName] = snapshot;
}
