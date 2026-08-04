import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  AdjustmentEntry,
  AdjustmentListResult,
  CreateInventoryItemRequest,
  InboundEntry,
  InventoryItem,
  InventoryListParams,
  InventoryListResponse,
  RecordAdjustmentRequest,
  RecordAdjustmentResult,
  RecordInboundRequest,
  RecordInboundResult,
  UpdateInventoryItemRequest
} from '../models/inventory.models';

/**
 * Thin wrapper over the `/inventory` endpoints (ACTION_PLAN E7-01…E7-04,
 * §15.3). All HTTP goes through the shared `ApiService` — no component talks
 * to `HttpClient` directly, following `VendorsService`'s convention.
 *
 * Gated on `Inventory.View` (reads) / `Inventory.Edit` (writes) — enforced
 * server-side; this service does not duplicate that check client-side.
 * `recordAdjustment` is the one exception: it sits behind its own
 * `Inventory.Adjust` policy (N-38, confirmed in `InventoryController`), not
 * `Inventory.Edit` — a business can grant "record physical counts" without
 * granting full item edit rights, so `InventoryComponent` gates that action
 * on a separate `canAdjust` check, not `canEdit`.
 */
@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly api = inject(ApiService);

  list(params: InventoryListParams): Observable<InventoryListResponse> {
    return this.api.get<InventoryListResponse>('/inventory', {
      search: params.search,
      categoryId: params.categoryId,
      vendorId: params.vendorId,
      stockLevel: params.stockLevel,
      page: params.page,
      pageSize: params.pageSize
    });
  }

  getById(id: string): Observable<InventoryItem> {
    return this.api.get<InventoryItem>(`/inventory/${id}`);
  }

  create(request: CreateInventoryItemRequest): Observable<InventoryItem> {
    return this.api.post<InventoryItem>('/inventory', request);
  }

  /** No `onHandQty` on `UpdateInventoryItemRequest` by design (D-42) — stock only moves via `recordInbound()` or a shipment line. */
  update(id: string, request: UpdateInventoryItemRequest): Observable<InventoryItem> {
    return this.api.put<InventoryItem>(`/inventory/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/inventory/${id}`);
  }

  /** Writes an `inventory_inbound_entries` row and increments `onHandQty` in one transaction; returns both the entry and the re-computed item (E7-02). */
  recordInbound(id: string, request: RecordInboundRequest): Observable<RecordInboundResult> {
    return this.api.post<RecordInboundResult>(`/inventory/${id}/inbound`, request);
  }

  listInbound(id: string): Observable<InboundEntry[]> {
    return this.api.get<InboundEntry[]>(`/inventory/${id}/inbound`);
  }

  /** Writes an `inventory_adjustments` row and sets `onHandQty` to `countedQty` in one transaction; returns both the adjustment and the re-computed item (N-38, `Inventory.Adjust`-gated — see this class's doc comment). */
  recordAdjustment(id: string, request: RecordAdjustmentRequest): Observable<RecordAdjustmentResult> {
    return this.api.post<RecordAdjustmentResult>(`/inventory/${id}/adjustments`, request);
  }

  /** Newest first per the N-38 contract. Unwraps the API's `{ items: [...] }` envelope (`InventoryStockAdjustmentListResultDto`) — unlike `listInbound`, this endpoint doesn't return a bare array. */
  listAdjustments(id: string): Observable<AdjustmentEntry[]> {
    return this.api.get<AdjustmentListResult>(`/inventory/${id}/adjustments`).pipe(map((res) => res.items));
  }
}
