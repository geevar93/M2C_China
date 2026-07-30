import { StockLevel } from '../models/inventory.models';

/** A background/foreground colour pair, matching `StatusStyleService`'s `StatusColor` shape. */
export interface StockLevelColor {
  bg: string;
  fg: string;
}

/**
 * Stock-level colour mapping is deliberately NOT routed through the shared
 * `StatusStyleService` (`shared/services/status-style.service.ts`) — that
 * service's `ST` map has no `HEALTHY`/`LOW`/`NEGATIVE` entries (they aren't a
 * lead/vendor/shipment status), so keying off it would silently fall back to
 * `ST.NEW`'s grey for every row. The approved prototype's own `invRows()`
 * (~line 1412) computes this same green/amber/red triple locally rather than
 * through its shared `ST` constant, for the same reason — it's a distinct
 * semantic scheme. Colours below are the design tokens' success/warning/danger
 * triple (docs/DESIGN_TOKENS.md §1), applied via CSS custom property names so
 * callers can bind them straight into `[style]`.
 */
const LEVEL_COLOR: Record<StockLevel, StockLevelColor> = {
  HEALTHY: { bg: 'var(--color-success-bg)', fg: 'var(--color-success)' },
  LOW: { bg: 'var(--color-warning-bg)', fg: 'var(--color-warning)' },
  NEGATIVE: { bg: 'var(--color-danger-bg)', fg: 'var(--color-danger)' }
};

export function stockLevelColor(level: StockLevel): StockLevelColor {
  return LEVEL_COLOR[level];
}

/** The On Hand column's number colour — healthy rows use the default text colour, not green (matches the prototype's `qtyColor`). */
export function stockLevelTextColor(level: StockLevel): string {
  if (level === 'NEGATIVE') return 'var(--color-danger)';
  if (level === 'LOW') return 'var(--color-warning)';
  return 'var(--color-text)';
}

/**
 * Low-stock bar width, ported verbatim from the prototype's `invRows()`
 * (`ratio = clamp(round((qty / (reorder * 2.5)) * 100), 0, 100)`, full width
 * when negative) — with a guard the prototype never needed because its mock
 * data never seeded a zero reorder threshold, but live data can (division by
 * zero would otherwise produce `NaN`/`Infinity`). A zero-threshold item has no
 * "below reorder" concept, so the bar reads as full when there's any stock at
 * all and empty when there is none.
 */
export function stockBarWidth(qty: number, reorder: number): number {
  if (qty < 0) return 100;
  if (reorder === 0) return qty > 0 ? 100 : 0;
  const ratio = Math.round((qty / (reorder * 2.5)) * 100);
  return Math.max(0, Math.min(100, ratio));
}
