const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;

/**
 * Formats an ISO 8601 UTC timestamp (`TimelineEvent.occurredAtUtc`) into the
 * prototype's exact display format, e.g. `18 Jul 2026 · 14:22`
 * (Source/Sourcing Ops Platform.dc.html `baseTimeline`, ~line 1481 — the
 * prototype's mock data pre-formats this string; the real API sends raw ISO
 * 8601 UTC instead, per the E4-15 task brief, so the frontend formats it).
 *
 * Rendered in the browser's local time zone (`Date`'s local getters) since
 * staff view this from India — deliberately not a fixed UTC render.
 */
export function formatTimelineDate(isoUtc: string | null | undefined): string {
  if (!isoUtc) return '—';
  const d = new Date(isoUtc);
  if (Number.isNaN(d.getTime())) return '—';

  const day = String(d.getDate()).padStart(2, '0');
  const month = MONTHS[d.getMonth()];
  const year = d.getFullYear();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `${day} ${month} ${year} · ${hh}:${mm}`;
}
