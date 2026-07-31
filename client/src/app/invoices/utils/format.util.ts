/**
 * Small formatting helpers owned by the invoices feature (E0-05, design-only
 * pass). Kept local to `invoices/` rather than added to `shared/utils` since
 * they exist only to render the mocked invoice screens — see
 * `../mock-invoices.ts` for why there is no live data to format here yet.
 */

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;

/**
 * `formatInr` now lives in `shared/utils/currency.util.ts` — re-exported here so
 * existing imports keep working. It moved when M5's inventory/shipment screens
 * became the first to format live money: the original "keep it local, it only
 * formats mocks" reasoning stopped applying, and a second copy is how two screens
 * end up disagreeing about what "₹" means.
 */
export { formatInr } from '../../shared/utils/currency.util';

/** `yyyy-MM-dd` → `10 Jul 2026`, matching the style of `shared/utils/date-format.util.ts`'s `formatTimelineDate` (date-only, no time component). */
export function formatInvoiceDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '—';
  const d = new Date(isoDate);
  if (Number.isNaN(d.getTime())) return '—';
  const day = String(d.getUTCDate()).padStart(2, '0');
  const month = MONTHS[d.getUTCMonth()];
  return `${day} ${month} ${d.getUTCFullYear()}`;
}
