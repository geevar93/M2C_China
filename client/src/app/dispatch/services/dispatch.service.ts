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
   * E9-01/E9-06/E9-10: fetches the rendered template message, the server-built
   * `wa.me` deep link, and the temporary public share link for a
   * customer/document pair. Do not construct the deep link on the client — see
   * `dispatch.models.ts`'s class doc.
   *
   * E9-10 note: this GET has a side effect — it mints a share-link row. That is
   * why it is gated on `Dispatch.Send` server-side and why the dialog calls it
   * on open and on selection change, not on every keystroke.
   */
  compose(customerId: string, catalogDocumentId: string): Observable<DispatchComposeResult> {
    return this.api.get<DispatchComposeResult>('/dispatch-log/compose', { customerId, catalogDocumentId });
  }

  /** E9-10: kills a share link before its expiry. Idempotent server-side. */
  revokeShareLink(shareLinkId: string): Observable<void> {
    return this.api.post<void>(`/dispatch-log/share-links/${shareLinkId}/revoke`, {});
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
