// Hand-drawn, not an icon library: three glyphs, each a genuinely different shape so
// status reads correctly in grayscale or for a colorblind viewer — not just "different
// color, same dot." No emoji anywhere in this app; these are the only status glyphs.

export function CheckCircleIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <circle cx="8" cy="8" r="6.75" fill="currentColor" opacity="0.16" />
      <circle cx="8" cy="8" r="6.75" stroke="currentColor" strokeWidth="1.4" />
      <path
        d="M5 8.2L7.1 10.3L11.2 6"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeLinejoin="round"
        fill="none"
      />
    </svg>
  );
}

export function WarningTriangleIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path
        d="M8 1.6L14.8 13.8H1.2L8 1.6Z"
        fill="currentColor"
        opacity="0.16"
        stroke="currentColor"
        strokeWidth="1.4"
        strokeLinejoin="round"
      />
      <rect x="7.35" y="5.6" width="1.3" height="3.9" rx="0.65" fill="currentColor" />
      <rect x="7.35" y="10.4" width="1.3" height="1.3" rx="0.65" fill="currentColor" />
    </svg>
  );
}

// A dashed, hollow circle with a slash — deliberately not a filled shape like the other
// two: Unreachable means "nothing answered," which is a different kind of unknown than
// Unhealthy's "it answered and it's bad."
export function UnreachableIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <circle cx="8" cy="8" r="6.25" stroke="currentColor" strokeWidth="1.4" strokeDasharray="2 2.2" />
      <line x1="4.1" y1="11.9" x2="11.9" y2="4.1" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}
