import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { VendorDetail, VendorsListParams, VendorsListResponse, VendorWriteRequest } from '../models/vendor.models';

/**
 * Thin wrapper over the `/vendors` endpoints (ACTION_PLAN E5-08/E5-09). All
 * HTTP goes through the shared ApiService — no component talks to
 * HttpClient directly.
 */
@Injectable({ providedIn: 'root' })
export class VendorsService {
  private readonly api = inject(ApiService);

  list(params: VendorsListParams): Observable<VendorsListResponse> {
    return this.api.get<VendorsListResponse>('/vendors', {
      search: params.search,
      page: params.page,
      pageSize: params.pageSize,
      categoryId: params.categoryId,
      region: params.region,
      statusId: params.statusId
    });
  }

  getById(id: string): Observable<VendorDetail> {
    return this.api.get<VendorDetail>(`/vendors/${id}`);
  }

  create(request: VendorWriteRequest): Observable<VendorDetail> {
    return this.api.post<VendorDetail>('/vendors', request);
  }

  update(id: string, request: VendorWriteRequest): Observable<VendorDetail> {
    return this.api.put<VendorDetail>(`/vendors/${id}`, request);
  }
}
