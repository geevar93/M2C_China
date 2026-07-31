/**
 * Wire contracts for the `/shipments` API surface (ACTION_PLAN E7-05…E7-09).
 *
 * **Diffed against live responses before these screens were wired** (D-22), not
 * transcribed from ACTION_PLAN §15.3. Follows the M4 vendor/catalog convention of
 * embedding the resolved lookup object rather than a bare id.
 */

import { StatusRef } from '../../shared/models/lookup-ref.models';

export interface CustomerRef {
  id: string;
  name: string;
}

export interface ShipmentListItem {
  id: string;
  /** Human-readable `SHP-YYMM-NNN`, generated server-side and never accepted from the caller (D-37). */
  reference: string | null;
  customer: CustomerRef;
  destination: string | null;
  serviceType: StatusRef;
  dispatchDate: string | null;
  status: StatusRef;
  freightCost: number | null;
  totalValue: number | null;
  mode: string | null;
  awbOrBl: string | null;
  eta: string | null;
  lineCount: number;
}

/**
 * One status tab. Counted across the filtered set **excluding the status filter
 * itself** (E7-08) — otherwise every tab would read either its own total or zero
 * once a tab was selected. This is why the tabs must not be recomputed from the
 * current page.
 */
export interface ShipmentStatusCount {
  statusId: string;
  code: string;
  label: string;
  sortOrder: number;
  count: number;
}

export interface ShipmentListResponse {
  items: ShipmentListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  statusCounts: ShipmentStatusCount[];
}

export interface ShipmentListParams {
  search?: string;
  statusId?: string;
  customerId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface ShipmentLine {
  id: string;
  inventoryItemId: string;
  inventoryItemName: string;
  inventoryItemSku: string | null;
  unit: string;
  quantity: number;
  /** Snapshotted at line creation (D-30) so a later item price change cannot rewrite an issued invoice's basis. */
  unitCost: number | null;
  lineTotal: number | null;
}

/** A timestamped transition (D-33). Renders the detail screen's stepper — the "when" under each step. */
export interface ShipmentStatusHistoryEntry {
  id: string;
  status: StatusRef;
  changedByUserId: string;
  changedByName: string;
  changedAt: string;
  note: string | null;
}

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

/** `GET /shipments/{id}` — every list-item field plus these five. */
export interface ShipmentDetail extends ShipmentListItem {
  createdAt: string;
  /** Derived from the earliest status-history row (D-47) — `shipments` has no `created_by_user_id`. */
  recordedByName: string | null;
  lines: ShipmentLine[];
  statusHistory: ShipmentStatusHistoryEntry[];
  documents: ShipmentDocument[];
}

/** `PUT /shipments/{id}/status` — the only path that writes status, and therefore history (D-43). */
export interface ChangeShipmentStatusRequest {
  statusId: string;
  note?: string;
}

/** One offending item in a 409 `insufficientStock` response (D-35). */
export interface InsufficientStockItem {
  inventoryItemId: string;
  itemName: string;
  sku: string | null;
  requestedQty: number;
  availableQty: number;
}
