import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  CatalogDocument,
  CatalogSection,
  CatalogSectionsListParams,
  CatalogSectionsListResponse,
  CreateCatalogSectionRequest,
  UpdateCatalogSectionRequest
} from '../models/catalog.models';

/**
 * Thin wrapper over the `/catalog-sections` and `/catalog-documents` API
 * surface (ACTION_PLAN E6-01…E6-08). All HTTP goes through the shared
 * ApiService — no component talks to HttpClient directly, including the
 * multipart upload and the authenticated binary download.
 */
@Injectable({ providedIn: 'root' })
export class CatalogsService {
  private readonly api = inject(ApiService);

  list(params: CatalogSectionsListParams): Observable<CatalogSectionsListResponse> {
    return this.api.get<CatalogSectionsListResponse>('/catalog-sections', {
      search: params.search,
      page: params.page,
      pageSize: params.pageSize,
      categoryId: params.categoryId,
      vendorId: params.vendorId,
      tag: params.tag
    });
  }

  getById(id: string): Observable<CatalogSection> {
    return this.api.get<CatalogSection>(`/catalog-sections/${id}`);
  }

  /** `vendorId` is required and immutable once set (see `catalog.models.ts`'s class doc — a real disagreement with the prototype). */
  create(request: CreateCatalogSectionRequest): Observable<CatalogSection> {
    return this.api.post<CatalogSection>('/catalog-sections', request);
  }

  /** Deliberately cannot change `vendorId` — `UpdateCatalogSectionRequest` has no such field. */
  update(id: string, request: UpdateCatalogSectionRequest): Observable<CatalogSection> {
    return this.api.put<CatalogSection>(`/catalog-sections/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/catalog-sections/${id}`);
  }

  /** Multipart PDF upload (E6-02); versioning (E6-03) is entirely server-decided — the client just posts the file. */
  uploadDocument(sectionId: string, file: File): Observable<CatalogDocument> {
    const formData = new FormData();
    formData.append('file', file);
    return this.api.postFormData<CatalogDocument>(`/catalog-sections/${sectionId}/documents`, formData);
  }

  /** Authenticated stream (E6-04/TECH_SPEC §8) — never a public static path. */
  downloadDocument(documentId: string): Observable<Blob> {
    return this.api.getBlob(`/catalog-documents/${documentId}/download`);
  }
}
