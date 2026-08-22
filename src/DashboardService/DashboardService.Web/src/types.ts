export type ServiceHealthStatus = "Healthy" | "Unhealthy" | "Unreachable";

// Mirrors DashboardService.Api.Contracts.ServiceStatusResponse.
export interface ServiceStatus {
  serviceName: string;
  baseUrl: string;
  status: ServiceHealthStatus;
  version: string | null;
  gitSha: string | null;
  buildTimeUtc: string | null;
  responseTimeMs: number | null;
  lastSuccessfulCheckUtc: string | null;
  errorMessage: string | null;
}
