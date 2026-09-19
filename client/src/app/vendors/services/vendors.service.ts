import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { VendorDetail, VendorDocument, VendorsListParams, VendorsListResponse, VendorWriteRequest } from '../models/vendor.models';

/**
 * Thin wrapper over the `/vendors` (+ `/vendor-documents`) endpoints
 * (ACTION_PLAN E5-08/E5-09/E5-10). All HTTP goes through the shared
 * ApiService — no component talks to HttpClient directly. Follows
 * `ShipmentsService`'s convention exactly: list/upload nest under
 * `/vendors/{id}/documents`, download/delete live on the separate
 * `/vendor-documents/{id}` resource root (`VendorDocumentsController`,
 * live-confirmed).
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

  /** Deletes the vendor with its catalog sections/documents and compliance documents. */
  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/vendors/${id}`);
  }

  /** The API wraps the rows in an `{ items }` envelope (`VendorDocumentListResultDto`); unwrapped here. */
  listDocuments(id: string): Observable<VendorDocument[]> {
    return this.api.get<{ items: VendorDocument[] }>(`/vendors/${id}/documents`).pipe(map((res) => res.items ?? []));
  }

  /** Multipart upload (ACTION_PLAN E5-10). The form field is `docTypeId` — `VendorsController.UploadDocument`'s `[FromForm] Guid docTypeId` parameter, NOT `documentTypeId` (that name is the shipment upload's, a distinct route). */
  uploadDocument(id: string, file: File, docTypeId: string): Observable<VendorDocument> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('docTypeId', docTypeId);
    return this.api.postFormData<VendorDocument>(`/vendors/${id}/documents`, formData);
  }

  /** NOT nested under `/vendors/{id}/...` — lives at `/vendor-documents/{id}/download` (live-confirmed route, `VendorDocumentsController`). */
  downloadDocument(documentId: string): Observable<Blob> {
    return this.api.getBlob(`/vendor-documents/${documentId}/download`);
  }

  /** NOT nested under `/vendors/{id}/...` — lives at `/vendor-documents/{id}` (live-confirmed route, `VendorDocumentsController`). */
  deleteDocument(documentId: string): Observable<void> {
    return this.api.delete<void>(`/vendor-documents/${documentId}`);
  }
}
