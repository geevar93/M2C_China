/**
 * Pure derivation helpers for the Dashboard screen (ACTION_PLAN E10-09).
 * Kept out of the component so every guard (divide-by-zero, empty series,
 * a `priorPeriodCount` of 0) is unit-testable in isolation and assertable
 * against literal expected strings, per the story's test brief.
 */

const AXIS_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;

export type DateRangeOption = 'Last 7 days' | 'Last 30 days' | 'This quarter';

export interface DateRangeBounds {
  fromDate: string;
  toDate: string;
}

/** `Date` -> `YYYY-MM-DD`, local calendar day (no time-zone shift). */
function toDateOnly(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/**
 * Resolves the date-range `<select>` value into `fromDate`/`toDate` bounds
 * for the six `/analytics/*` calls. "This quarter" starts on the 1st of the
 * current calendar quarter (Jan/Apr/Jul/Oct), matching the FSD's fiscal-free
 * "quarter" wording (no fiscal-year offset defined anywhere in the schema).
 */
export function resolveDateRange(option: DateRangeOption, now: Date = new Date()): DateRangeBounds {
  const toDate = toDateOnly(now);

  if (option === 'Last 7 days') {
    const from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6);
    return { fromDate: toDateOnly(from), toDate };
  }
  if (option === 'This quarter') {
    const quarterStartMonth = Math.floor(now.getMonth() / 3) * 3;
    const from = new Date(now.getFullYear(), quarterStartMonth, 1);
    return { fromDate: toDateOnly(from), toDate };
  }
  // 'Last 30 days'
  const from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 29);
  return { fromDate: toDateOnly(from), toDate };
}

/** The KPI label suffix the prototype uses (`· 7d`, `· 30d`) — extended here to cover the quarter option. */
export function rangeSuffix(option: DateRangeOption): string {
  if (option === 'Last 7 days') return '7d';
  if (option === 'This quarter') return 'qtr';
  return '30d';
}

/**
 * `(current, prior)` -> a signed whole-percent change, or `null` when it
 * can't be computed. `prior === 0` is the guard the story calls out
 * explicitly: `(current - 0) / 0` is `Infinity` (or `NaN` when current is
 * also 0), and a dashboard must never render either — a real `0%` and "not
 * computable" are different facts.
 */
export function percentChange(current: number, prior: number): number | null {
  if (prior === 0) return null;
  return Math.round(((current - prior) / prior) * 100);
}

/** `conversionRate` arrives as 0..1 — formats as the prototype's `18.0%`, one decimal place. */
export function formatConversionRate(rate: number): string {
  return `${(rate * 100).toFixed(1)}%`;
}

/**
 * A value as a percentage of the largest value in its series (the
 * prototype's `w` field), for CSS bar widths. Guards two ways: an empty
 * series (`max` of nothing) and an all-zero series (`max === 0`, which
 * would otherwise divide by zero) both resolve to `0`, not `NaN`/`Infinity`.
 */
export function barWidthPct(value: number, max: number): number {
  if (max <= 0) return 0;
  return Math.max(0, Math.min(100, Math.round((value / max) * 100)));
}

/** Largest `count`-like field across a list, or `0` for an empty list — the shared guard `barWidthPct`'s `max` argument is built from. */
export function maxOf<T>(items: readonly T[], select: (item: T) => number): number {
  return items.reduce((max, item) => Math.max(max, select(item)), 0);
}

/** SVG donut-chart `stroke-dasharray` for a two-segment ring (the prototype's CIF/freight-only split), `r=46`. Guards `total === 0` to a zero-length arc rather than `NaN`. */
export function donutDashArray(part: number, total: number, radius = 46): string {
  const circumference = 2 * Math.PI * radius;
  const fraction = total > 0 ? part / total : 0;
  return `${Math.round(circumference * fraction)} ${Math.round(circumference)}`;
}

export interface LinePoint {
  x: number;
  y: number;
}

/**
 * Maps a count series onto the prototype's `640x180` line-chart viewBox
 * (`Source/Sourcing Ops Platform.dc.html` ~line 100-106): `x` spans
 * `5..635`, `y` spans `175` (max value) down to `20` (zero), baseline `179`.
 * Guards: a single-point series would divide by `(length - 1) === 0` — placed
 * at the horizontal centre instead. An all-zero series would divide by
 * `max === 0` — flattened to the `179` baseline instead of `NaN`.
 */
export function linePoints(series: readonly { count: number }[]): LinePoint[] {
  if (series.length === 0) return [];
  const max = maxOf(series, (p) => p.count);
  return series.map((p, i) => {
    const x = series.length === 1 ? 320 : Math.round((i / (series.length - 1)) * 630) + 5;
    const y = max === 0 ? 179 : Math.round(175 - (p.count / max) * 155);
    return { x, y };
  });
}

/** `points` -> the SVG `<polyline points="...">` attribute value. */
export function toPolylinePoints(points: readonly LinePoint[]): string {
  return points.map((p) => `${p.x},${p.y}`).join(' ');
}

/** `2026-08-03` -> `3 Aug` — compact axis label, no year (the chart spans weeks/months at most). */
export function formatAxisDate(periodStart: string): string {
  const d = new Date(periodStart);
  if (Number.isNaN(d.getTime())) return '';
  return `${d.getUTCDate()} ${AXIS_MONTHS[d.getUTCMonth()]}`;
}

/**
 * Up to 5 evenly-spaced indices into a series (first, ~25%, ~50%, ~75%,
 * last), for the x-axis labels under the line chart — the prototype hard-codes
 * 5 week labels (`W18`, `W21`, ...) over its fixed 12-point mock; this
 * generalises to whatever length the real series comes back as, de-duplicated
 * for series shorter than 5 points.
 */
export function axisLabelIndices(length: number): number[] {
  if (length === 0) return [];
  if (length <= 5) return Array.from({ length }, (_, i) => i);
  const steps = [0, 0.25, 0.5, 0.75, 1];
  const indices = steps.map((s) => Math.round(s * (length - 1)));
  return Array.from(new Set(indices));
}

/** Pluralizes a simple count-noun phrase — `1 shipment` vs `2 shipments`. */
export function pluralize(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
