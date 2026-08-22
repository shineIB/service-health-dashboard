namespace DashboardService.Domain;

// Written by the background poller, read by the API — never the other way around, and
// the API never triggers a poll itself. See ServiceHealthPollingService for why.
public interface IServiceHealthCache
{
    IReadOnlyList<ServiceHealthSnapshot> GetAll();
    ServiceHealthSnapshot? Get(string serviceName);
    void Set(ServiceHealthSnapshot snapshot);
}
