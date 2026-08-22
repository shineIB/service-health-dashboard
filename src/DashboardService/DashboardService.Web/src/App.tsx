import { useEffect, useState } from "react";
import type { ServiceHealthStatus, ServiceStatus } from "./types";
import { fetchServiceStatuses } from "./api";

// Matches dashboard-api's own poll interval (Polling:IntervalSeconds) — no point
// polling faster than the data can actually change.
const POLL_INTERVAL_MS = 5000;

const STATUS_COLOR: Record<ServiceHealthStatus, string> = {
  Healthy: "#1a7f37",
  Unhealthy: "#9a6700",
  Unreachable: "#cf222e",
};

function formatTimestamp(value: string | null): string {
  if (!value) return "never";
  return new Date(value).toLocaleString();
}

export default function App() {
  const [services, setServices] = useState<ServiceStatus[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      try {
        const data = await fetchServiceStatuses();
        if (!cancelled) {
          setServices(data);
          setError(null);
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

  return (
    <div style={{ fontFamily: "sans-serif", padding: "1rem" }}>
      <h1>Service Health Dashboard</h1>
      {error && (
        <p style={{ color: "#cf222e" }}>Failed to load service statuses: {error}</p>
      )}
      <table cellPadding={8} style={{ borderCollapse: "collapse", width: "100%" }}>
        <thead>
          <tr style={{ textAlign: "left", borderBottom: "2px solid #ccc" }}>
            <th>Service</th>
            <th>Status</th>
            <th>Version</th>
            <th>Git SHA</th>
            <th>Build time</th>
            <th>Response time</th>
            <th>Last successful check</th>
          </tr>
        </thead>
        <tbody>
          {services.map((service) => (
            <tr key={service.serviceName} style={{ borderBottom: "1px solid #eee" }}>
              <td>{service.serviceName}</td>
              <td style={{ color: STATUS_COLOR[service.status], fontWeight: "bold" }}>
                {service.status}
                {service.errorMessage && (
                  <div style={{ fontWeight: "normal", fontSize: "0.8em", color: "#666" }}>
                    {service.errorMessage}
                  </div>
                )}
              </td>
              <td>{service.version ?? "—"}</td>
              <td>{service.gitSha ?? "—"}</td>
              <td>{service.buildTimeUtc ?? "—"}</td>
              <td>{service.responseTimeMs !== null ? `${service.responseTimeMs} ms` : "—"}</td>
              <td>{formatTimestamp(service.lastSuccessfulCheckUtc)}</td>
            </tr>
          ))}
          {services.length === 0 && !error && (
            <tr>
              <td colSpan={7}>No services reported yet.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
