/**
 * Wire contracts for the `/inventory` API surface (ACTION_PLAN E7-01…E7-04).
 *
 * **Diffed against a live response before these screens were wired**, not
 * transcribed from ACTION_PLAN §15.3 — §15.3 is a written record, and building
 * a screen against an assumed DTO is this project's most-repeated defect (D-22,
 * and M1's only two real defects).
 *
 * Follows the M4 vendor/catalog convention of embedding the resolved lookup
 * object rather than a bare id, matching what the endpoint actually serialises.
 * `category` uses `name` where everything else uses `code`/`label` — the real DB
 * asymmetry TECH_SPEC §6 documents, carried faithfully rather than normalised.
 */

import { CategoryRef } from '../../shared/models/lookup-ref.models';

/** `{ id, name }` — vendors embed as a name reference, and are nullable on an item. */
export interface VendorRef {
  id: string;
  name: string;
}

/**
 * Classified server-side (E7-04) exactly as the prototype's `invRows()` does:
 * negative → `NEGATIVE`, else below reorder → `LOW`, else `HEALTHY`. The raw
 * `onHandQty`/`reorderThreshold` come alongside it so the visual bar's ratio
 * maths stays in the component, where presentation belongs.
 */
export type StockLevel = 'HEALTHY' | 'LOW' | 'NEGATIVE';

export interface InventoryItem {
  id: string;
  name: string;
  sku: string | null;
  description: string | null;
  category: CategoryRef;
  vendor: VendorRef | null;
  unit: string;
  onHandQty: number;
  reorderThreshold: number;
  unitCost: number | null;
  /**
   * COMPUTED `onHandQty * unitCost`, never stored (D-30). **`null` means the item
   * is not costed — not that it is worth zero.** Rendering a null as `₹0` would
   * silently misstate the On-Hand Value tile it rolls up into.
   */
  stockValue: number | null;
  stockLevel: StockLevel;
}

/**
 * Aggregated over the **whole filtered set, not the current page** (D-39) — it
 * backs the four stat tiles, which would be meaningless page-scoped. Honours the
 * caller's active filters, which is what distinguishes it from E10-05's
 * business-wide analytics aggregation (M7).
 */
export interface InventorySummary {
  onHandValue: number;
  itemCount: number;
  lowStockCount: number;
  negativeStockCount: number;
}

export interface InventoryListResponse {
  items: InventoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  summary: InventorySummary;
}

export interface InventoryListParams {
  search?: string;
  categoryId?: string;
  vendorId?: string;
  /**
   * `low` covers **below reorder OR negative** — one option, matching the
   * prototype's single "Low or negative" dropdown entry and its `i.qty < i.reorder`
   * predicate (E7-03). Deliberately not split into two filters the design doesn't have.
   */
  stockLevel?: 'low' | 'healthy';
  page?: number;
  pageSize?: number;
}

/** An inbound stock receipt (E7-02 / D-32) — a durable business record, not just an audit row. */
export interface InventoryInboundEntry {
  id: string;
  inventoryItemId: string;
  quantity: number;
  entryDate: string;
  reference: string | null;
  recordedByUserId: string;
  recordedByName: string;
  createdAt: string;
}

/** `POST /inventory/{id}/inbound` returns both halves so the caller re-renders without a second round trip. */
export interface RecordInboundResponse {
  entry: InventoryInboundEntry;
  item: InventoryItem;
}

export interface RecordInboundRequest {
  quantity: number;
  entryDate: string;
  reference?: string;
}
