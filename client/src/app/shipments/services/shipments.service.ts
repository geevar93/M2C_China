import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  ChangeShipmentStatusRequest,
  ShipmentDetail,
  ShipmentListParams,
  ShipmentListResponse
} from '../models/shipment.models';

/**
 * Thin wrapper over the `/shipments` endpoints (ACTION_PLAN E7-05…E7-09). All
 * HTTP goes through the shared ApiService — no component talks to HttpClient
 * directly.
 */
@Injectable({ providedIn: 'root' })
export class ShipmentsService {
  private readonly api = inject(ApiService);

  list(params: ShipmentListParams): Observable<ShipmentListResponse> {
    return this.api.get<ShipmentListResponse>('/shipments', {
      search: params.search,
      statusId: params.statusId,
      customerId: params.customerId,
      from: params.from,
      to: params.to,
      page: params.page,
      pageSize: params.pageSize
    });
  }

  getById(id: string): Observable<ShipmentDetail> {
    return this.api.get<ShipmentDetail>(`/shipments/${id}`);
  }

  /**
   * The **only** path that writes a shipment's status (D-43). A general update
   * deliberately cannot set `statusId`, because that would produce transitions with
   * no history row and a stepper with gaps.
   */
  changeStatus(id: string, request: ChangeShipmentStatusRequest): Observable<ShipmentDetail> {
    return this.api.put<ShipmentDetail>(`/shipments/${id}/status`, request);
  }
}
