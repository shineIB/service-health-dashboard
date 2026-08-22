import { useEffect, useState } from "react";
import type { ServiceStatus } from "./types";
import { fetchServiceStatuses } from "./api";
import { formatRelativeTime } from "./lib/relativeTime";
import { StatusBadge } from "./components/StatusBadge";
import "./App.css";

// Matches dashboard-api's own poll interval (Polling:IntervalSeconds) — no point
// polling faster than the data can actually change.
const POLL_INTERVAL_MS = 5000;

export default function App() {
  const [services, setServices] = useState<ServiceStatus[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [lastFetchAt, setLastFetchAt] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      try {
        const data = await fetchServiceStatuses();
        if (!cancelled) {
          setServices(data);
          setError(null);
          setLastFetchAt(Date.now());
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Unknown error");
        }
      }
    }

    void poll();
    const interval = setInterval(() => void poll(), POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  // A separate 1s tick, independent of the 5s data poll above: keeps the relative-time
  // cells and the next-check countdown accurate between polls without re-fetching.
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(tick);
  }, []);

  const updatedSecondsAgo = lastFetchAt !== null ? Math.max(0, Math.round((now - lastFetchAt) / 1000)) : null;
  const secondsUntilNext =
    lastFetchAt !== null ? Math.max(0, Math.ceil((lastFetchAt + POLL_INTERVAL_MS - now) / 1000)) : null;

  return (
    <div className="page">
      <header className="page-header">
        <h1>Service Health Dashboard</h1>
        <div className="poll-indicator" aria-live="polite">
          {lastFetchAt !== null
            ? `Updated ${updatedSecondsAgo}s ago · next check in ${secondsUntilNext}s`
            : "Loading…"}
        </div>
      </header>

      {error && <p className="fetch-error">Failed to load service statuses: {error}</p>}

      <table className="service-table">
        <thead>
          <tr>
            <th>Service</th>
            <th>Status</th>
            <th>Version</th>
            <th>Response time</th>
            <th>Last successful check</th>
          </tr>
        </thead>
        <tbody>
          {services.map((service) => (
            <tr key={service.serviceName} className={`row row--${service.status.toLowerCase()}`}>
              <td className="service-name">{service.serviceName}</td>
              <td>
                <StatusBadge status={service.status} />
              </td>
              <td className="mono">
                {service.version ?? "—"}
                {service.gitSha && <span className="git-sha"> @{service.gitSha.slice(0, 7)}</span>}
              </td>
              <td className="mono">
                {service.responseTimeMs !== null ? `${service.responseTimeMs} ms` : "—"}
              </td>
              <td className="mono">{formatRelativeTime(service.lastSuccessfulCheckUtc, now)}</td>
            </tr>
          ))}
          {services.length === 0 && !error && (
            <tr>
              <td colSpan={5} className="empty-state">
                No services reported yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
