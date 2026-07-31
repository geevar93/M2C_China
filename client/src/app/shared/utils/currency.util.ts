/**
 * `en-IN` currency formatting via the built-in `Intl` API — no new dependency
 * (constraint C1 / TECH_SPEC §11).
 *
 * Promoted to `shared/` for the M5 inventory and shipment screens, which are the
 * first to render *live* money. `invoices/utils/format.util.ts` previously owned
 * `formatInr` and kept it local on the stated grounds that it only ever formatted
 * mocked data; that reason no longer holds, and two copies of a currency format
 * is how two screens end up disagreeing about what "₹" means.
 */

/** `250000` → `₹2,50,000.00`. Full precision — for table cells and detail fields. */
export function formatInr(amount: number): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(amount);
}

/**
 * `4120000` → `₹41.2 L`. The abbreviated form the approved prototype's stat tiles
 * use ("₹41.2 L"), in Indian lakh/crore units rather than K/M — a headline number
 * read at a glance, not a figure anyone reconciles against.
 *
 * Thresholds are the conventional ones: ≥1 crore in Cr, ≥1 lakh in L, ≥1 thousand
 * in K, below that the plain rupee figure. Negatives keep their sign (stock value
 * can legitimately go negative once an item is oversold, D-35).
 */
export function formatInrCompact(amount: number): string {
  const sign = amount < 0 ? '-' : '';
  const abs = Math.abs(amount);

  if (abs >= 10000000) return `${sign}₹${trimZero(abs / 10000000)} Cr`;
  if (abs >= 100000) return `${sign}₹${trimZero(abs / 100000)} L`;
  if (abs >= 1000) return `${sign}₹${trimZero(abs / 1000)} K`;
  return `${sign}₹${Math.round(abs)}`;
}

/** One decimal place, but `41.0` reads as `41` — the prototype writes "₹41.2 L", not "₹41.20 L". */
function trimZero(value: number): string {
  return value.toFixed(1).replace(/\.0$/, '');
}

/** Quantities: `1250` → `1,250` in Indian digit grouping, matching the prototype's `toLocaleString('en-IN')`. */
export function formatQty(value: number): string {
  return new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 }).format(value);
}
