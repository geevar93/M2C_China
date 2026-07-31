/**
 * Rupee formatting, shared across every feature that renders money.
 *
 * Consolidated here by the M5 screen pass. Three byte-identical copies of the
 * same `Intl.NumberFormat('en-IN', …)` call had accumulated — one in
 * `invoices/utils/format.util.ts` (E0-05), one in `inventory/utils/`, one in
 * `shipments/utils/money.util.ts`. The last two were written concurrently by
 * separate agents that had each been told to stay inside their own feature
 * folder, so the duplication was an artefact of that file-ownership split
 * rather than a design decision. Sits alongside `date-format.util.ts`, which
 * is already the established home for cross-feature formatting helpers.
 *
 * Currency is hard-coded to INR: FSD Phase 1 is a single-currency business and
 * nothing in the schema carries a currency code. If that ever changes, this is
 * the one place it changes.
 */

const INR = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
});

/** `250000` → `₹2,50,000.00` — Indian lakh/crore digit grouping, via the built-in `Intl` API (no new dependency, C1). */
export function formatInr(amount: number): string {
  return INR.format(amount);
}

/**
 * The nullable variant, for the several money fields that are genuinely
 * **not costed** rather than worth zero — `stockValue`, `unitCost`,
 * `lineTotal`, `freightCost`, `totalValue` (D-30). Renders an em-dash, never
 * `₹0.00`, because those two mean different things to the business and the
 * screens must not blur them.
 */
export function formatMoneyOrDash(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '—';
  return INR.format(value);
}

/**
 * `4120000` → `₹41.2 L`. The **abbreviated** form the approved prototype's stat
 * tiles use (`'₹41.2 L'`, Source/Sourcing Ops Platform.dc.html ~line 1572), in
 * Indian lakh/crore units rather than K/M.
 *
 * Carried across during the M5 consolidation: the surviving inventory screen
 * rendered its On-Hand Value tile at full precision (`₹41,20,000.00`), which is
 * correct arithmetic but not a 1:1 port of a screen whose acceptance criterion is
 * exactly that. Tiles are read at a glance, not reconciled — the table below them
 * carries the precise figures.
 *
 * Negatives keep their sign: on-hand value can legitimately go negative once an
 * item is oversold (D-35).
 */
export function formatInrCompact(amount: number): string {
  const sign = amount < 0 ? '-' : '';
  const abs = Math.abs(amount);

  if (abs >= 10000000) return `${sign}₹${trimZero(abs / 10000000)} Cr`;
  if (abs >= 100000) return `${sign}₹${trimZero(abs / 100000)} L`;
  if (abs >= 1000) return `${sign}₹${trimZero(abs / 1000)} K`;
  return `${sign}₹${Math.round(abs)}`;
}

/** One decimal place, but `41.0` reads as `41` — the prototype writes `₹41.2 L`, not `₹41.20 L`. */
function trimZero(value: number): string {
  return value.toFixed(1).replace(/\.0$/, '');
}
