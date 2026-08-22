// "4s ago", not an ISO timestamp — the whole point is reading it at a glance.
export function formatRelativeTime(iso: string | null, nowMs: number): string {
  if (!iso) return "never";

  const diffSeconds = Math.max(0, Math.round((nowMs - new Date(iso).getTime()) / 1000));
  if (diffSeconds < 5) return "just now";
  if (diffSeconds < 60) return `${diffSeconds}s ago`;

  const minutes = Math.floor(diffSeconds / 60);
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}
