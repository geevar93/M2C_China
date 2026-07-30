import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  CreateInventoryItemRequest,
  InboundEntry,
  InventoryItem,
  InventoryListParams,
  InventoryListResponse,
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
}
