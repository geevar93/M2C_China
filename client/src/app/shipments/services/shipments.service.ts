import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  ChangeShipmentStatusRequest,
  CreateShipmentRequest,
  ShipmentDetail,
  ShipmentDocument,
  ShipmentsListParams,
  ShipmentsListResponse,
  UpdateShipmentRequest
} from '../models/shipment.models';

/**
 * Thin wrapper over the `/shipments` (+ `/shipment-documents`) endpoints
 * (ACTION_PLAN E7-05…E7-10, §15.3). All HTTP goes through the shared
 * `ApiService` — no component talks to `HttpClient` directly, following
 * `VendorsService`'s/`CatalogsService`'s convention, including the
 * multipart upload and the authenticated binary download.
 *
 * Gated on `Shipments.View` (reads) / `Shipments.Edit` (writes) — enforced
 * server-side.
 *
 * Two deliberate request-shape omissions, both load-bearing (see
 * `shipment.models.ts`'s doc comments for the full reasoning, D-42/D-43):
 * `UpdateShipmentRequest` has no `statusId` — status only ever changes via
 * `changeStatus()`, the one path that also writes a history row.
 */
@Injectable({ providedIn: 'root' })
export class ShipmentsService {
  private readonly api = inject(ApiService);

  list(params: ShipmentsListParams): Observable<ShipmentsListResponse> {
    return this.api.get<ShipmentsListResponse>('/shipments', {
      statusId: params.statusId,
      customerId: params.customerId,
      from: params.from,
      to: params.to,
      search: params.search,
      page: params.page,
      pageSize: params.pageSize
    });
  }

  /** Returns the full `ShipmentDetail` shape, not the list shape (lines/statusHistory/documents included). */
  getById(id: string): Observable<ShipmentDetail> {
    return this.api.get<ShipmentDetail>(`/shipments/${id}`);
  }

  /**
   * Returns the full `ShipmentDetail` shape too (live-confirmed) — creation
   * auto-seeds one `statusHistory` row with note `"Shipment created."`. May
   * 409 with an `insufficientStock` ProblemDetails extension (D-35); retry
   * with `allowNegativeStock: true` on the request to override deliberately.
   */
  create(request: CreateShipmentRequest): Observable<ShipmentDetail> {
    return this.api.post<ShipmentDetail>('/shipments', request);
  }

  /** No `statusId` on `UpdateShipmentRequest` by design (D-43) — use `changeStatus()`. May also 409 with `insufficientStock` on a line-quantity increase. */
  update(id: string, request: UpdateShipmentRequest): Observable<ShipmentDetail> {
    return this.api.put<ShipmentDetail>(`/shipments/${id}`, request);
  }

  /** Deleting restores whatever the shipment's lines had consumed (D-38 — stock movement is delta-based, not a full re-decrement). */
  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/shipments/${id}`);
  }

  /** The ONLY path that writes `status` and the `shipment_status_history` row (E7-07). Transitioning to the status already held is rejected. */
  changeStatus(id: string, request: ChangeShipmentStatusRequest): Observable<ShipmentDetail> {
    return this.api.put<ShipmentDetail>(`/shipments/${id}/status`, request);
  }

  listDocuments(id: string): Observable<ShipmentDocument[]> {
    // The API wraps the rows in an `{ items }` envelope (`ShipmentDocumentListResultDto`).
    return this.api.get<{ items: ShipmentDocument[] }>(`/shipments/${id}/documents`).pipe(map((res) => res.items ?? []));
  }

  /** Multipart upload, reusing the E1-05 validator server-side (E7-09). */
  uploadDocument(id: string, file: File, documentTypeId: string): Observable<ShipmentDocument> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('documentTypeId', documentTypeId);
    return this.api.postFormData<ShipmentDocument>(`/shipments/${id}/documents`, formData);
  }

  /** NOT nested under `/shipments/{id}/...` — lives at `/shipment-documents/{id}/download` (live-confirmed route). */
  downloadDocument(documentId: string): Observable<Blob> {
    return this.api.getBlob(`/shipment-documents/${documentId}/download`);
  }

  /** NOT nested under `/shipments/{id}/...` — lives at `/shipment-documents/{id}` (live-confirmed route). */
  deleteDocument(documentId: string): Observable<void> {
    return this.api.delete<void>(`/shipment-documents/${documentId}`);
  }
}
