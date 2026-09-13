/**
 * Wire contracts for the `/invoices` + `/admin/company-settings` API surface
 * (ACTION_PLAN E8-01…E8-08, §17.3 — the M6 backend handoff artifact).
 *
 * Every shape below was **diffed against a live running API** before any
 * component was wired (the §15.6 gate that caught D-50 in M5), not assumed
 * from the written contract. Live-observed details that are easy to get wrong
 * by assumption:
 *
 *  - `invoiceDate` serialises as a **bare date string** (`"2026-08-03"`), NOT
 *    a full ISO instant — unlike shipments' `dispatchDate`/`eta`, which do
 *    carry a time. Do not reuse a shipment date formatter here.
 *  - `paidAt`/`createdAt`/`changedAt` ARE full UTC instants
 *    (`"2026-08-03T06:25:33.1766816Z"`). The two kinds of date coexist in one
 *    DTO; that asymmetry is deliberate and matches the DB columns.
 *  - `customer.name` on the embedded ref resolves to the customer's
 *    **business name** when they have one (live: `"Ramesh Traders Pvt Ltd"`
 *    for a customer whose `name` is `"Ramesh Traders"`). It is already the
 *    right string to print on a "Bill to" block — do not re-derive it.
 *  - Money fields are decimals (`125000.0`), never integers.
 *  - `totalAmount` is computed server-side (`amount + taxAmount`) and sent on
 *    the wire. Never recompute it client-side — it is the server's number.
 *  - `hasPdf` is a boolean. The storage path is deliberately never
 *    serialised; the PDF comes from the authenticated download route only.
 */

import { CustomerRef, StatusRef } from '../../shared/models/lookup-ref.models';

/** The four seeded invoice status codes (E3-07). Switch on `code`, NEVER on `label` — labels are Super-Admin editable (D-50). */
export type InvoiceStatusCode = 'DRAFT' | 'ISSUED' | 'PAID' | 'CANCELLED';

/** An `invoice_status_history` row — renders the detail screen's lifecycle trail. */
export interface InvoiceStatusHistoryEntry {
  id: string;
  status: StatusRef;
  changedByUserId: string;
  changedByName: string;
  changedAt: string;
  note: string | null;
}

/** An invoice list row (E8-05). */
export interface InvoiceListItem {
  id: string;
  invoiceNumber: string;
  customer: CustomerRef;
  /** Resolved from the CUSTOMER's service type, not the shipment's — a freight-only invoice has no shipment at all. */
  serviceType: StatusRef;
  status: StatusRef;
  /** Bare date string, e.g. `"2026-08-03"`. */
  invoiceDate: string;
  amount: number;
  taxAmount: number;
  /** Server-computed `amount + taxAmount`. */
  totalAmount: number;
  currency: string;
  shipmentId: string | null;
  shipmentReference: string | null;
  hasPdf: boolean;
  /** Full UTC instant, or null while unpaid. */
  paidAt: string | null;
}

/** List row + line description, creator, paid reference and full history — one call, no second round trip. */
export interface InvoiceDetail extends InvoiceListItem {
  lineDescription: string | null;
  createdByUserId: string;
  createdByName: string;
  createdAt: string;
  paidReference: string | null;
  statusHistory: InvoiceStatusHistoryEntry[];
  lines: InvoiceLine[];
  taxSummary: InvoiceTaxSummary;
}

/**
 * Per-status count across the whole filtered set **excluding the status filter
 * itself** (E7-08's semantics, reused verbatim). Zero-count statuses ARE
 * included and the array is ordered by `sortOrder`, so status tabs can be
 * rendered straight from this without client-side padding or sorting.
 *
 * `totalAmount` (N-31) is the sum of `amount + taxAmount` for that status,
 * computed server-side over the whole filtered set (every filter except the
 * status filter) — NOT the current page. It backs the invoice list's two
 * money stat-tiles ("Total issued" = ISSUED + PAID, "Total outstanding" =
 * ISSUED only), which is why those tiles stay filter-independent of the
 * status tab: every entry in this array already reflects the same
 * status-excluded filtered set regardless of which tab is active. Select
 * entries by `code`, NEVER `label` (labels are Super-Admin-editable master
 * data — D-50).
 */
export interface InvoiceStatusCount {
  statusId: string;
  code: InvoiceStatusCode | string;
  label: string;
  sortOrder: number;
  count: number;
  totalAmount: number;
}

export interface InvoiceListResponse {
  items: InvoiceListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  statusCounts: InvoiceStatusCount[];
}

export interface InvoiceListParams {
  search?: string;
  page?: number;
  pageSize?: number;
  customerId?: string;
  statusId?: string;
  serviceTypeId?: string;
  /** Bare date strings, e.g. `"2026-08-01"`. */
  fromDate?: string;
  toDate?: string;
}

/**
 * One billable line as it comes back from the API. Every money figure here is
 * **computed server-side** by the one GST implementation — never re-derive
 * them in the client, or the screen and the PDF can disagree.
 *
 * On a DRAFT the split is provisional: place of supply is only fixed at issue,
 * so `cgstAmount`/`sgstAmount`/`igstAmount` reflect a working assumption until
 * `taxSummary.isIntraState` is non-null.
 */
export interface InvoiceLine {
  id: string;
  inventoryItemId: string | null;
  description: string;
  /** HSN (goods) or SAC (services). Required before the invoice can be issued. */
  hsnCode: string | null;
  quantity: number;
  /** Per-unit price EXCLUSIVE of GST — tax is added on top, never backed out. */
  unitPrice: number;
  /** Percent, e.g. `18` for 18%. Required before the invoice can be issued. */
  gstRate: number | null;
  taxableValue: number;
  cgstAmount: number;
  sgstAmount: number;
  igstAmount: number;
  lineTotal: number;
  sortOrder: number;
}

/** Taxable value and tax grouped by rate — the rate-wise summary a GST invoice carries. */
export interface InvoiceTaxRateBreakdown {
  gstRate: number;
  taxableValue: number;
  cgstAmount: number;
  sgstAmount: number;
  igstAmount: number;
}

/**
 * Invoice-level tax block. `isIntraState` is **null on a DRAFT** — place of
 * supply is only settled at issue — so render it as provisional rather than
 * presenting a guess as final.
 */
export interface InvoiceTaxSummary {
  placeOfSupplyStateCode: string | null;
  placeOfSupplyStateName: string | null;
  isIntraState: boolean | null;
  taxableValue: number;
  cgstAmount: number;
  sgstAmount: number;
  igstAmount: number;
  totalTax: number;
  rateBreakdown: InvoiceTaxRateBreakdown[];
}

/**
 * One line as it goes OUT. `unitPrice`, `hsnCode` and `gstRate` are all
 * nullable: send just an `inventoryItemId` and a `quantity` and the server
 * fills them from the item. A value that IS sent wins, so an operator can
 * override a price or slab per line.
 *
 * Nothing is invented when both the request and the item are silent — the
 * field stays null and the issue-time check refuses. In particular the server
 * NEVER falls back to the item's cost price.
 */
export interface UpsertInvoiceLineRequest {
  inventoryItemId: string | null;
  description: string | null;
  hsnCode: string | null;
  quantity: number;
  unitPrice: number | null;
  gstRate: number | null;
}

/**
 * E8-01. Deliberately has NO `statusId` — every invoice is created DRAFT
 * server-side — and no `invoiceNumber`/`pdfFilePath`, both server-owned.
 * `shipmentId` is nullable: CIF invoices reference a shipment, freight-only
 * invoices stand alone. When supplied it must belong to `customerId`.
 *
 * `amount`/`taxAmount` are **not** part of this request. Both are derived from
 * `lines` server-side; sending them would assert a total that could disagree
 * with the lines backing it.
 */
export interface CreateInvoiceRequest {
  customerId: string;
  shipmentId: string | null;
  invoiceDate: string;
  lineDescription: string | null;
  currency: string;
  lines: UpsertInvoiceLineRequest[];
}

/**
 * E8-01/E8-02. Editable ONLY while DRAFT — the API returns **409** otherwise.
 * Has no `statusId` by design (status moves only via `changeStatus`) and no
 * paid marker (that is `markPaid`, which carries its own permission). Mirrors
 * `UpdateShipmentRequest`'s D-42/D-43 precedent exactly.
 */
export type UpdateInvoiceRequest = CreateInvoiceRequest;

export interface ChangeInvoiceStatusRequest {
  statusId: string;
  note: string | null;
}

export interface MarkInvoicePaidRequest {
  /** Full UTC instant; omit/null to let the server default it to now. */
  paidAt: string | null;
  paidReference: string | null;
}

/**
 * The company billing singleton (E8-08). Returned **all-null when unset**
 * rather than 404 (D-70), so "not configured yet" is a real, renderable
 * state — FSD Q9c's values are still outstanding.
 *
 * **This is not merely cosmetic.** `legalEntityName` and `registeredAddress`
 * are REQUIRED to issue an invoice: attempting DRAFT → ISSUED without them
 * returns **400** with an `errors.companySettings` message, live-confirmed.
 * The UI must surface that as a solvable configuration gap, not a failure.
 */
export interface CompanySettings {
  legalEntityName: string | null;
  gstin: string | null;
  /**
   * Two-digit GST state code of the SELLER. Decides CGST+SGST (buyer in the
   * same state) versus IGST (different states) on every invoice issued, so
   * the API **refuses DRAFT → ISSUED with a 400 when it is null** and cannot
   * be derived from `gstin`'s first two digits.
   */
  stateCode: string | null;
  /** Server-resolved display name for `stateCode`, e.g. `"Gujarat"`. Read-only. */
  stateName: string | null;
  registeredAddress: string | null;
  bankAccountName: string | null;
  bankAccountNumber: string | null;
  bankIfsc: string | null;
  bankBranch: string | null;
  invoiceNumberPrefix: string | null;
  declarationText: string | null;
  updatedAt: string | null;
  updatedByName: string | null;
}

/** `stateName` is server-derived, so it is never sent back. */
export type UpsertCompanySettingsRequest = Omit<CompanySettings, 'updatedAt' | 'updatedByName' | 'stateName'>;

/**
 * True when every company field the API requires before an invoice may be
 * issued is present. `stateCode` joined the set with the GST work: without it
 * the server cannot decide the tax treatment and refuses rather than guessing.
 */
export function canIssueInvoices(settings: CompanySettings | null | undefined): boolean {
  return (
    !!settings?.legalEntityName?.trim() &&
    !!settings?.registeredAddress?.trim() &&
    !!settings?.stateCode?.trim()
  );
}
