import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  InventoryItem,
  InventoryListParams,
  InventoryListResponse,
  RecordInboundRequest,
  RecordInboundResponse
} from '../models/inventory.models';

/**
 * Thin wrapper over the `/inventory` endpoints (ACTION_PLAN E7-01…E7-04). All
 * HTTP goes through the shared ApiService — no component talks to HttpClient
 * directly.
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

  /**
   * The only way stock goes up (D-42) — `PUT /inventory/{id}` deliberately cannot
   * change `onHandQty`, so an edit form must never offer one.
   */
  recordInbound(id: string, request: RecordInboundRequest): Observable<RecordInboundResponse> {
    return this.api.post<RecordInboundResponse>(`/inventory/${id}/inbound`, request);
  }
}
