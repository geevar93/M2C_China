/**
 * Quantity formatting owned by the inventory feature. Currency formatting is
 * deliberately NOT here — it lives in `shared/utils/money.util.ts`, which this
 * file used to duplicate before the M5 screen pass consolidated the three
 * identical copies that had accumulated across invoices, inventory and
 * shipments.
 */

import { formatMoneyOrDash } from '../../shared/utils/money.util';

/**
 * Quantity formatting for `onHandQty`/`reorderThreshold`/inbound `quantity` —
 * all decimals on the wire (see `inventory.models.ts`'s header comment), so
 * this must never assume a whole number the way the prototype's
 * `qty.toLocaleString('en-IN')` implicitly did against its integer mock data.
 */
export function formatQty(value: number): string {
  return value.toLocaleString('en-IN', { maximumFractionDigits: 2 });
}

/** `stockValue: null` means "not costed", never "worth zero" (D-30) — renders an em-dash, not `₹0.00`. */
export function formatStockValue(value: number | null): string {
  return formatMoneyOrDash(value);
}
