import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { CreateDispatchLogRequest, DispatchComposeResult, DispatchHistoryEntryDto, DispatchLogDto } from '../models/dispatch.models';

/**
 * Thin wrapper over the `/dispatch-log` and `/catalog-documents/{id}/dispatches`
 * API surface (ACTION_PLAN E9). All HTTP goes through the shared ApiService —
 * no component talks to HttpClient directly.
 */
@Injectable({ providedIn: 'root' })
export class DispatchService {
  private readonly api = inject(ApiService);

  /**
   * E9-01/E9-06: fetches the rendered template message and the server-built
   * `wa.me` deep link for a customer/document pair. Do not construct the
   * deep link on the client — see `dispatch.models.ts`'s class doc.
   */
  compose(customerId: string, catalogDocumentId: string): Observable<DispatchComposeResult> {
    return this.api.get<DispatchComposeResult>('/dispatch-log/compose', { customerId, catalogDocumentId });
  }

  /** E9-02: records a dispatch. The staff user comes from the token server-side — never sent here. */
  create(request: CreateDispatchLogRequest): Observable<DispatchLogDto> {
    return this.api.post<DispatchLogDto>('/dispatch-log', request);
  }

  /** E9-07: newest-first "sent to" history for one catalog document. */
  history(catalogDocumentId: string): Observable<DispatchHistoryEntryDto[]> {
    return this.api.get<DispatchHistoryEntryDto[]>(`/catalog-documents/${catalogDocumentId}/dispatches`);
  }
}
