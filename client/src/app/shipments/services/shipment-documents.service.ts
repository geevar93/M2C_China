import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ShipmentDocument } from '../models/shipment.models';

/**
 * Shipment reference documents (ACTION_PLAN E7-09). A separate service from
 * `ShipmentsService` because the download/delete endpoints hang off their own
 * `/shipment-documents` resource root, mirroring how `CatalogDocumentsController`
 * splits from `CatalogSectionsController` server-side.
 */
@Injectable({ providedIn: 'root' })
export class ShipmentDocumentsService {
  private readonly api = inject(ApiService);

  list(shipmentId: string): Observable<ShipmentDocument[]> {
    return this.api.get<ShipmentDocument[]>(`/shipments/${shipmentId}/documents`);
  }

  /**
   * `documentTypeId` must be a **Shipment-scoped** type — the server rejects a
   * vendor-scoped one with a 400 rather than filing it quietly (D-34). The caller's
   * dropdown is filtered to matching scope so a user cannot pick an invalid one.
   */
  upload(shipmentId: string, file: File, documentTypeId: string): Observable<ShipmentDocument> {
    const form = new FormData();
    form.append('file', file);
    form.append('documentTypeId', documentTypeId);
    return this.api.postFormData<ShipmentDocument>(`/shipments/${shipmentId}/documents`, form);
  }

  /** Served only through this authenticated, permission-checked endpoint — never a public static path (TECH_SPEC §8). */
  download(documentId: string): Observable<Blob> {
    return this.api.getBlob(`/shipment-documents/${documentId}/download`);
  }

  delete(documentId: string): Observable<void> {
    return this.api.delete<void>(`/shipment-documents/${documentId}`);
  }
}
