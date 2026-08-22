import type { ComponentType } from "react";
import type { ServiceHealthStatus } from "../types";
import { CheckCircleIcon, WarningTriangleIcon, UnreachableIcon } from "./icons";

const STATUS_META: Record<ServiceHealthStatus, { label: string; Icon: ComponentType<{ size?: number }> }> = {
  Healthy: { label: "Healthy", Icon: CheckCircleIcon },
  Unhealthy: { label: "Unhealthy", Icon: WarningTriangleIcon },
  Unreachable: { label: "Unreachable", Icon: UnreachableIcon },
};

// Status is encoded three ways at once — color, icon shape, and fill style (solid vs.
// dashed/hollow for Unreachable) — so it still reads correctly in grayscale or for a
// colorblind viewer, not only by hue.
export function StatusBadge({ status }: { status: ServiceHealthStatus }) {
  const { label, Icon } = STATUS_META[status];
  return (
    <span className={`status-badge status-badge--${status.toLowerCase()}`}>
      <Icon size={15} />
      {label}
    </span>
  );
}
