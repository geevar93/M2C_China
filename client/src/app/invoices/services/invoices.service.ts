import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  ChangeInvoiceStatusRequest,
  CompanySettings,
  CreateInvoiceRequest,
  InvoiceDetail,
  InvoiceListParams,
  InvoiceListResponse,
  MarkInvoicePaidRequest,
  UpdateInvoiceRequest,
  UpsertCompanySettingsRequest
} from '../models/invoice.models';

/**
 * Thin wrapper over the `/invoices` + `/admin/company-settings` endpoints
 * (ACTION_PLAN E8-01…E8-08, §17.3). All HTTP goes through the shared
 * `ApiService` — no component talks to `HttpClient` directly — following
 * `ShipmentsService`'s convention, including the authenticated binary
 * download.
 *
 * Gated on `Invoicing.View` (reads) / `Invoicing.Edit` (writes) /
 * `Invoicing.MarkPaid` (the paid marker only), all enforced server-side.
 *
 * **Three request shapes deliberately omit fields**, each load-bearing:
 *  - `CreateInvoiceRequest` has no `statusId` — invoices are always born
 *    DRAFT server-side, and there is no "→ DRAFT" transition to get back.
 *  - `UpdateInvoiceRequest` has no `statusId` — status moves only via
 *    `changeStatus()`, the one path that also writes a history row.
 *  - Neither carries a paid marker — that is `markPaid()` alone, so its
 *    separate `Invoicing.MarkPaid` permission cannot be bypassed through the
 *    general edit or status endpoints.
 *
 * **409 is an expected, meaningful response here**, not an error to swallow
 * (D-69). It means the request was well-formed but the invoice's current
 * state disallows it, and the caller may legitimately retry later. Callers
 * should surface `detail` from the ProblemDetails body; the payload also
 * carries `currentStatus` (edit/PDF cases) or `fromStatus`/`toStatus`
 * (transition cases) as extension members.
 */
@Injectable({ providedIn: 'root' })
export class InvoicesService {
  private readonly api = inject(ApiService);

  list(params: InvoiceListParams): Observable<InvoiceListResponse> {
    return this.api.get<InvoiceListResponse>('/invoices', {
      search: params.search,
      page: params.page,
      pageSize: params.pageSize,
      customerId: params.customerId,
      statusId: params.statusId,
      serviceTypeId: params.serviceTypeId,
      fromDate: params.fromDate,
      toDate: params.toDate
    });
  }

  /** Returns the full `InvoiceDetail` shape — lineDescription/statusHistory included, no second call needed. */
  get(id: string): Observable<InvoiceDetail> {
    return this.api.get<InvoiceDetail>(`/invoices/${id}`);
  }

  create(request: CreateInvoiceRequest): Observable<InvoiceDetail> {
    return this.api.post<InvoiceDetail>('/invoices', request);
  }

  /** 409 if the invoice is no longer DRAFT. */
  update(id: string, request: UpdateInvoiceRequest): Observable<InvoiceDetail> {
    return this.api.put<InvoiceDetail>(`/invoices/${id}`, request);
  }

  /**
   * DRAFT → ISSUED is the transition that renders and stores the PDF, and it
   * returns **400** (not 409) with `errors.companySettings` when the company
   * billing block has not been configured — see `canIssueInvoices`.
   */
  changeStatus(id: string, request: ChangeInvoiceStatusRequest): Observable<InvoiceDetail> {
    return this.api.put<InvoiceDetail>(`/invoices/${id}/status`, request);
  }

  /** Only reachable from ISSUED — 409 from any other status. */
  markPaid(id: string, request: MarkInvoicePaidRequest): Observable<InvoiceDetail> {
    return this.api.post<InvoiceDetail>(`/invoices/${id}/mark-paid`, request);
  }

  /**
   * Authenticated PDF download. 409 when the invoice has never been issued,
   * so guard on `hasPdf` before offering the action.
   * Live-confirmed filename: `Content-Disposition: attachment; filename=INV-2608-001.pdf`.
   */
  downloadPdf(id: string): Observable<Blob> {
    return this.api.getBlob(`/invoices/${id}/pdf`);
  }

  /** All-null when never configured (D-70) — that is a 200, not a 404. */
  getCompanySettings(): Observable<CompanySettings> {
    return this.api.get<CompanySettings>('/admin/company-settings');
  }

  /** Super-Admin only. Blank strings are trimmed to null server-side. */
  upsertCompanySettings(request: UpsertCompanySettingsRequest): Observable<CompanySettings> {
    return this.api.put<CompanySettings>('/admin/company-settings', request);
  }
}
