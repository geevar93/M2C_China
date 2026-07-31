/**
 * Wire contracts for the `/shipments` (+ `/shipment-documents`) API surface
 * (ACTION_PLAN E7-05…E7-10, §15.3 — the M5 backend handoff artifact). Shapes
 * below are as the live API actually serialises them, diffed against a
 * running instance, not assumed from the written contract (D-22).
 *
 * Two live-observed shapes that are easy to get wrong by assumption:
 *  - `dispatchDate`/`eta` serialise as **full ISO-8601 with time**
 *    (`"2026-07-22T00:00:00Z"`) even though they are logically dates.
 *  - Quantity/money fields are decimals, not integers (`quantity: 500.0`,
 *    `availableQty: 10.000`) — never assume a whole number when formatting.
 *
 * Document endpoints live at `/shipment-documents/{id}/download` and
 * `DELETE /shipment-documents/{id}` — NOT nested under `/shipments/{id}/...`
 * for those two verbs, only for list/upload.
 */

import { CustomerRef, StatusRef } from '../../shared/models/lookup-ref.models';

/** `code`/`label` service-type ref embedded on a shipment — same shape as `StatusRef`, distinct meaning. */
export interface ServiceTypeRef {
  id: string;
  code: string;
  label: string;
}

/** A `shipment_status_history` row (D-33) — renders the detail screen's 4-step stepper, including the "when" under each step. */
export interface ShipmentStatusHistoryEntry {
  id: string;
  status: StatusRef;
  changedByUserId: string;
  changedByName: string;
  changedAt: string;
  note: string | null;
}

/** A `shipment_documents` row (E7-09; D-41 added `sizeBytes`/`uploadedByUserId`). */
export interface ShipmentDocument {
  id: string;
  shipmentId: string;
  originalFilename: string;
  sizeBytes: number;
  documentType: StatusRef;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
}

/** A shipment line — `unitCost`/`lineTotal` are snapshotted at line creation (D-30b), never read live off the inventory item. */
export interface ShipmentLine {
  id: string;
  inventoryItemId: string;
  inventoryItemName: string;
  inventoryItemSku: string | null;
  unit: string;
  quantity: number;
  unitCost: number | null;
  lineTotal: number | null;
}

/** `GET /shipments` items[] shape and every list column `GET /shipments/{id}` also returns. */
export interface ShipmentListItem {
  id: string;
  /** Server-generated `SHP-YYMM-NNN` (D-37) — never accepted from the caller. Nullable only because the type predates seeing a shipment that lacks one; live responses always populate it. */
  reference: string | null;
  customer: CustomerRef;
  destination: string | null;
  serviceType: ServiceTypeRef;
  /** Full ISO-8601 with time, even though logically a date — see module doc comment. */
  dispatchDate: string | null;
  status: StatusRef;
  freightCost: number | null;
  /** Server-computed when the shipment has lines; accepted from the caller only when it has none (the freight-only case, D-31). */
  totalValue: number | null;
  mode: string | null;
  awbOrBl: string | null;
  /** Full ISO-8601 with time — see module doc comment. */
  eta: string | null;
  lineCount: number;
}

/** Per-status row of `GET /shipments`' `statusCounts` — backs the status tabs. */
export interface ShipmentStatusCount {
  statusId: string;
  code: string;
  label: string;
  sortOrder: number;
  count: number;
}

export interface ShipmentsListResponse {
  items: ShipmentListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  /**
   * Live-confirmed: includes ZERO-count statuses, and is computed EXCLUDING
   * the active status filter itself (filtering to `PACKED` still reported an
   * `IN TRANSIT` count of 2) — so every tab always shows a real total, never
   * its own filtered total or zero.
   */
  statusCounts: ShipmentStatusCount[];
}

/** `GET /shipments` query params, live-confirmed. */
export interface ShipmentsListParams {
  statusId?: string;
  customerId?: string;
  from?: string;
  to?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

/** `GET /shipments/{id}` — every `ShipmentListItem` field plus these. Live-confirmed: `POST /shipments` also returns this full detail shape, not the list shape, with one `statusHistory` row auto-seeded (note: "Shipment created."). */
export interface ShipmentDetail extends ShipmentListItem {
  createdAt: string;
  /** Reads the earliest `shipment_status_history` row (D-47) — there is no `created_by_user_id` column on `shipments`. */
  recordedByName: string | null;
  lines: ShipmentLine[];
  statusHistory: ShipmentStatusHistoryEntry[];
  documents: ShipmentDocument[];
}

export interface CreateShipmentLineRequest {
  inventoryItemId: string;
  quantity: number;
}

/**
 * Body for `POST /shipments`. Create DOES take an initial `statusId` — only
 * update forbids changing it (see `UpdateShipmentRequest`). `FREIGHT_ONLY`
 * shipments must send no lines (D-36: a 400 otherwise) and move no stock;
 * `totalValue` is accepted only when there are no lines (D-31).
 */
export interface CreateShipmentRequest {
  customerId: string;
  destination?: string | null;
  serviceTypeId: string;
  dispatchDate?: string | null;
  statusId: string;
  freightCost?: number | null;
  /** Only honoured when `lines` is empty (D-31) — otherwise server-computed and any caller value is ignored. */
  totalValue?: number | null;
  mode?: string | null;
  awbOrBl?: string | null;
  eta?: string | null;
  lines: CreateShipmentLineRequest[];
  /** D-35 override: set `true` to deliberately allow a decrement that would drive on-hand quantity negative. */
  allowNegativeStock?: boolean;
}

/**
 * Body for `PUT /shipments/{id}`. Deliberately has **no `statusId`** (D-43,
 * live-verified: sending a different `statusId` left status at `PACKED`).
 * `PUT /shipments/{id}/status` is the only path that writes status, and the
 * only one that writes a `shipment_status_history` row. An edit form that
 * includes a status dropdown will silently fail to change it.
 */
export interface UpdateShipmentRequest {
  customerId: string;
  destination?: string | null;
  serviceTypeId: string;
  dispatchDate?: string | null;
  freightCost?: number | null;
  totalValue?: number | null;
  mode?: string | null;
  awbOrBl?: string | null;
  eta?: string | null;
  lines: CreateShipmentLineRequest[];
  allowNegativeStock?: boolean;
}

/** Body for `PUT /shipments/{id}/status` — the one path that writes status + history (E7-07). Transitioning to the status already held is rejected. */
export interface ChangeShipmentStatusRequest {
  statusId: string;
  note?: string | null;
}

/** One row of the 409 `insufficientStock` ProblemDetails extension (D-35), live-confirmed exactly as §15.3 documents. */
export interface InsufficientStockItem {
  inventoryItemId: string;
  itemName: string;
  sku: string | null;
  requestedQty: number;
  availableQty: number;
}

/**
 * RFC 7807 ProblemDetails body extension for the 409 raised by
 * `POST`/`PUT /shipments` when a decrement would drive on-hand quantity
 * negative. Retry with `allowNegativeStock: true` on the request to
 * override deliberately (D-35).
 */
export interface InsufficientStockProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  insufficientStock: InsufficientStockItem[];
}
