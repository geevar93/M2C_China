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

/**
 * Date-only variant of `formatTimelineDate` (no `· HH:mm` suffix), e.g.
 * `14 Jul 2026` — used where the source only carries a date's worth of
 * meaning (a catalog document's upload date, a vendor's created date), so
 * showing a time would imply more precision than the field has.
 */
export function formatDateOnly(isoUtc: string | null | undefined): string {
  if (!isoUtc) return '—';
  const d = new Date(isoUtc);
  if (Number.isNaN(d.getTime())) return '—';

  const day = String(d.getDate()).padStart(2, '0');
  const month = MONTHS[d.getMonth()];
  const year = d.getFullYear();
  return `${day} ${month} ${year}`;
}

export interface FollowUpDue {
  /** True once the due date's *calendar day* (local time) is before today's. */
  overdue: boolean;
  /** e.g. "Due today", "Overdue by 1 day", "Overdue by 3 days". */
  label: string;
}

/**
 * Classifies a follow-up's due date relative to `now` for the due-follow-ups
 * reminder screen (ACTION_PLAN E4-08 / FSD Q2). Compares local calendar days,
 * not raw milliseconds — a follow-up due earlier today must read as "Due
 * today", not "Overdue by 0 days" or a fraction-of-a-day artefact. The backend
 * only ever returns rows whose `follow_up_date <= asOf`, so `diffDays` is
 * expected to be >= 0 in practice; a negative value (clock skew) is treated
 * the same as today rather than throwing.
 */
export function describeFollowUpDue(followUpDateIso: string | null | undefined, now: Date = new Date()): FollowUpDue {
  const due = followUpDateIso ? new Date(followUpDateIso) : null;
  if (!due || Number.isNaN(due.getTime())) return { overdue: false, label: '—' };

  const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const dayMs = 24 * 60 * 60 * 1000;
  const diffDays = Math.round((startOfDay(now) - startOfDay(due)) / dayMs);

  if (diffDays <= 0) return { overdue: false, label: 'Due today' };
  if (diffDays === 1) return { overdue: true, label: 'Overdue by 1 day' };
  return { overdue: true, label: `Overdue by ${diffDays} days` };
}
