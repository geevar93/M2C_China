/**
 * Date formatting owned by the invoices feature (E0-05, design-only pass).
 *
 * `formatInr` used to live here too; the M5 screen pass moved it to
 * `shared/utils/money.util.ts` once inventory and shipments each grew their
 * own identical copy. It is re-exported below so the invoice screens' existing
 * import sites keep working unchanged — this file stays their single
 * formatting entry point.
 */

export { formatInr } from '../../shared/utils/money.util';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;

/** `yyyy-MM-dd` → `10 Jul 2026`, matching the style of `shared/utils/date-format.util.ts`'s `formatTimelineDate` (date-only, no time component). */
export function formatInvoiceDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '—';
  const d = new Date(isoDate);
  if (Number.isNaN(d.getTime())) return '—';
  const day = String(d.getUTCDate()).padStart(2, '0');
  const month = MONTHS[d.getUTCMonth()];
  return `${day} ${month} ${d.getUTCFullYear()}`;
}
