import type { ServiceStatus } from "./types";

export async function fetchServiceStatuses(): Promise<ServiceStatus[]> {
  const response = await fetch("/api/services");
  if (!response.ok) {
    throw new Error(`Failed to fetch service statuses: HTTP ${response.status}`);
  }
  return response.json();
}
