/**
 * Wire contracts for the `/dispatch-log` and `/catalog-documents/{id}/dispatches`
 * API surface (ACTION_PLAN E9), verified against the real backend
 * (`SourcingOps.Application.Dispatching.DispatchDtos`, built concurrently by
 * the backend track). All gated on `Dispatch.Send` except history, which is
 * gated on `Catalogs.View` (see `DispatchHistoryEntry`'s doc comment).
 */

/**
 * `GET /dispatch-log/compose?customerId=&catalogDocumentId=` response — the
 * rendered template default (still editable per E9-06) and the ready-built
 * `wa.me` deep link (E9-01). **Never build the deep link client-side** — that
 * logic deliberately lives behind a server-side interface
 * (`IDispatchMessageSender`) so a Business-API sender can replace it in
 * Phase 2 (E9-09) without touching this screen.
 */
export interface DispatchComposeResult {
  message: string;
  deepLinkUrl: string;
}

/**
 * `POST /dispatch-log` body. Deliberately has **no staff field** — the server
 * takes the staff user from the token (`CreateDispatchLogRequest` on the
 * backend), never the request body.
 */
export interface CreateDispatchLogRequest {
  customerId: string;
  catalogDocumentId: string;
  message: string;
}

/** The recorded dispatch, with display names resolved so no second round trip is needed. */
export interface DispatchLogDto {
  id: string;
  customerId: string;
  customerName: string;
  catalogDocumentId: string;
  catalogName: string;
  catalogDocumentFilename: string;
  staffUserId: string;
  staffUserName: string;
  message: string;
  sentAtUtc: string;
}

/**
 * E9-07: one row of a catalog document's "sent to" history — which customer,
 * which staff member, and when. `GET /catalog-documents/{id}/dispatches`
 * returns these newest first.
 */
export interface DispatchHistoryEntryDto {
  dispatchId: string;
  customerId: string;
  customerName: string;
  staffUserId: string;
  staffUserName: string;
  sentAtUtc: string;
}
