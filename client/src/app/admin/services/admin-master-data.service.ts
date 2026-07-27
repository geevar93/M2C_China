import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  COLLECTION_SEGMENTS,
  MasterDataAggregate,
  MasterDataCollectionKey,
  MasterDataRow,
  ReorderItem,
  UpsertMasterDataRequest
} from '../models/admin-master-data.models';

/**
 * Thin wrapper over the existing `/master-data` endpoints (E0-04b, backed by
 * the M2 `MasterDataController` — no backend change). All HTTP goes through
 * the shared `ApiService`; the component never talks to `HttpClient`
 * directly (follows `CustomersService`'s convention).
 *
 * Distinct from `core/services/master-data.service.ts`: that service caches
 * the aggregate for read-only lookup consumers and only exposes active
 * options. This service is the admin write-path — every mutation plus a
 * fetch that always includes retired rows, since the admin screen needs to
 * both show and act on retired rows in the same table.
 */
@Injectable({ providedIn: 'root' })
export class AdminMasterDataService {
  private readonly api = inject(ApiService);

  getAggregate(includeRetired = true): Observable<MasterDataAggregate> {
    return this.api.get<MasterDataAggregate>('/master-data', { includeRetired });
  }

  create(key: MasterDataCollectionKey, request: UpsertMasterDataRequest): Observable<MasterDataRow> {
    return this.api.post<MasterDataRow>(`/master-data/${COLLECTION_SEGMENTS[key]}`, request);
  }

  /** PUT never changes `code` server-side (D-12) even if the caller sends one. */
  update(key: MasterDataCollectionKey, id: string, request: UpsertMasterDataRequest): Observable<MasterDataRow> {
    return this.api.put<MasterDataRow>(`/master-data/${COLLECTION_SEGMENTS[key]}/${id}`, request);
  }

  retire(key: MasterDataCollectionKey, id: string): Observable<MasterDataRow> {
    return this.api.post<MasterDataRow>(`/master-data/${COLLECTION_SEGMENTS[key]}/${id}/retire`, {});
  }

  restore(key: MasterDataCollectionKey, id: string): Observable<MasterDataRow> {
    return this.api.post<MasterDataRow>(`/master-data/${COLLECTION_SEGMENTS[key]}/${id}/restore`, {});
  }

  reorder(key: MasterDataCollectionKey, items: ReorderItem[]): Observable<MasterDataRow[]> {
    return this.api.put<MasterDataRow[]>(`/master-data/${COLLECTION_SEGMENTS[key]}/reorder`, items);
  }

  /** May 409 with a ProblemDetails `detail` reading "...Retire it instead of
   * deleting." for a referenced row — the caller surfaces that verbatim. */
  delete(key: MasterDataCollectionKey, id: string): Observable<void> {
    return this.api.delete<void>(`/master-data/${COLLECTION_SEGMENTS[key]}/${id}`);
  }
}
