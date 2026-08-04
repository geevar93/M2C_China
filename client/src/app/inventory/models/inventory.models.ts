/**
 * Wire contracts for the `/inventory` API surface (ACTION_PLAN E7-01…E7-04,
 * §15.3 — the M5 backend handoff artifact). Shapes below are as the live API
 * actually serialises them, diffed against a running instance, not assumed
 * from the written contract — see §15.3's own warning that an assumed DTO is
 * this project's most-repeated defect (D-22).
 *
 * Two things §15.3's literal example JSON gets slightly wrong, corrected here:
 *  - Every quantity/money field serialises as a **decimal**, not an integer
 *    (`onHandQty: 1840.0`, `reorderThreshold: 600.0`, `quantity: 500.0`,
 *    `availableQty: 10.000`). Typed `number` below either way (TS has no
 *    int/decimal distinction), but a screen must never assume these are
 *    whole numbers when formatting or parsing user input.
 *  - The list endpoint's default `pageSize` is **25**, not the `20` shown in
 *    §15.3's example literal.
 */

import { CategoryRef, VendorRef } from '../../shared/models/lookup-ref.models';

export type StockLevel = 'HEALTHY' | 'LOW' | 'NEGATIVE';

/** `GET|POST|PUT /inventory`, `GET|PUT|DELETE /inventory/{id}` (`Inventory.View`/`Inventory.Edit`). */
export interface InventoryItem {
  id: string;
  name: string;
  sku: string | null;
  description: string | null;
  category: CategoryRef;
  /** Nullable — an item need not carry a vendor. */
  vendor: VendorRef | null;
  unit: string;
  onHandQty: number;
  reorderThreshold: number;
  unitCost: number | null;
  /** COMPUTED `onHandQty * unitCost` server-side. `null` means "not costed", NOT "worth zero" (D-30). */
  stockValue: number | null;
  stockLevel: StockLevel;
}

/** Stat-tile summary over the WHOLE filtered set (D-39), not just the current page. */
export interface InventorySummary {
  onHandValue: number;
  itemCount: number;
  lowStockCount: number;
  negativeStockCount: number;
}

/** `GET /inventory` query params — all live-confirmed against a running instance. */
export interface InventoryListParams {
  search?: string;
  categoryId?: string;
  vendorId?: string;
  stockLevel?: StockLevel;
  page?: number;
  pageSize?: number;
}

export interface InventoryListResponse {
  items: InventoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  /** Honours the caller's active filters (live-confirmed: `?stockLevel=LOW` returned `itemCount: 1`, `onHandValue` over that one item only) — never the unfiltered whole table. */
  summary: InventorySummary;
}

/**
 * Body for `POST /inventory`. An opening `onHandQty` is settable once, here,
 * at create — the only other way stock ever changes is an inbound entry or a
 * shipment line (D-42).
 */
export interface CreateInventoryItemRequest {
  name: string;
  sku?: string | null;
  description?: string | null;
  categoryId: string;
  vendorId?: string | null;
  unit: string;
  onHandQty: number;
  reorderThreshold: number;
  unitCost?: number | null;
}

/**
 * Body for `PUT /inventory/{id}`. Deliberately has **no `onHandQty`** (D-42,
 * live-verified: sending `onHandQty: 999999` left the stored value
 * unchanged). Stock moves only through `recordInbound()` or a shipment line
 * — an edit form that renders an editable on-hand field will silently drop
 * whatever the user typed into it.
 */
export interface UpdateInventoryItemRequest {
  name: string;
  sku?: string | null;
  description?: string | null;
  categoryId: string;
  vendorId?: string | null;
  unit: string;
  reorderThreshold: number;
  unitCost?: number | null;
}

/** A durable, queryable business record (D-32) — distinct from the audit log. */
export interface InboundEntry {
  id: string;
  inventoryItemId: string;
  quantity: number;
  /** Bare date string (`"2026-07-29"`), NOT full ISO-8601 with time — unlike `dispatchDate`/`eta` on shipments. Live-confirmed. */
  entryDate: string;
  reference: string | null;
  recordedByUserId: string;
  recordedByName: string;
  createdAt: string;
}

/** Body for `POST /inventory/{id}/inbound`. */
export interface RecordInboundRequest {
  quantity: number;
  entryDate: string;
  reference?: string | null;
}

/** `POST /inventory/{id}/inbound` returns both the new entry and the re-computed item, so the caller re-renders both without a second round trip. */
export interface RecordInboundResult {
  entry: InboundEntry;
  item: InventoryItem;
}

/**
 * A durable, queryable record of a post-physical-count correction (N-38,
 * `POST|GET /inventory/{id}/adjustments`). Distinct from `InboundEntry`:
 * this reconciles `onHandQty` to what was actually counted — up, down, or
 * unchanged — rather than adding newly received stock, and it always
 * carries a `reason` (server-required). `delta` (`countedQty - previousQty`)
 * can be zero; a zero-delta count is still a valid, recorded adjustment
 * (N-38), not a no-op.
 */
export interface AdjustmentEntry {
  id: string;
  countedQty: number;
  previousQty: number;
  delta: number;
  reason: string;
  /** Bare date string (`"2026-07-29"`) — the physical count date, mirrors `InboundEntry.entryDate`'s shape. */
  adjustedOn: string;
  /** Full ISO-8601 timestamp of when the adjustment was recorded, distinct from `adjustedOn` (the counted date, which may be backdated). */
  adjustedAt: string;
  adjustedByUserId: string;
  adjustedByName: string;
}

/**
 * Body for `POST /inventory/{id}/adjustments`. `reason` is required
 * server-side — a blank value returns 400 with field key `reason` (N-38).
 * `adjustedOn` is optional; the server defaults it to today when omitted.
 * Only `countedQty` is constrained to be non-negative (400 otherwise) — the
 * item's on-hand quantity itself may legitimately be negative (oversold
 * items, D-39/D-42 territory), so adjusting *from* a negative current
 * quantity is normal and must not be blocked client-side.
 */
export interface RecordAdjustmentRequest {
  countedQty: number;
  reason: string;
  adjustedOn?: string;
}

/**
 * `POST /inventory/{id}/adjustments` returns both the new adjustment and the
 * re-computed item (confirmed against `RecordAdjustmentResultDto` once the
 * backend landed alongside this — matches the `RecordInboundResult`
 * convention this was modelled on before that).
 */
export interface RecordAdjustmentResult {
  adjustment: AdjustmentEntry;
  item: InventoryItem;
}

/** `GET /inventory/{id}/adjustments` wraps its rows in an `items` envelope (`InventoryStockAdjustmentListResultDto`) — unlike `listInbound`, which returns a bare array. Gated on `Inventory.View`, same as the rest of the read surface: viewing history isn't restricted the way recording a new adjustment is (`Inventory.Adjust`, distinct from `Inventory.Edit` — see `InventoryService.recordAdjustment`'s doc comment). */
export interface AdjustmentListResult {
  items: AdjustmentEntry[];
}
