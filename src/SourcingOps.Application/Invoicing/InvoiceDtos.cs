using SourcingOps.Application.Common;

namespace SourcingOps.Application.Invoicing;

/// <summary>
/// One timestamped invoice status transition (E8-02, M6 contract §5). Mirrors
/// <c>ShipmentStatusHistoryDto</c> exactly.
/// </summary>
public sealed record InvoiceStatusHistoryDto(
    Guid Id,
    StatusRefDto Status,
    Guid ChangedByUserId,
    string ChangedByName,
    DateTime ChangedAt,
    string? Note);

/// <summary>
/// An invoice list row (E8-05). M6 contract §3: embeds resolved lookup objects, matching the
/// M4/M5 convention — never bare lookup ids. <see cref="ServiceType"/> is resolved from the
/// CUSTOMER's service type, not the shipment's (a freight-only invoice has no shipment at
/// all). <see cref="TotalAmount"/> is computed (<c>Amount + TaxAmount</c>), never stored.
/// <see cref="HasPdf"/> is <c>PdfFilePath != null</c> — the path itself never appears in any
/// DTO (M6 contract §3); the client hits the authenticated <c>GET /invoices/{id}/pdf</c>
/// instead. <see cref="InvoiceDate"/> is a bare date string on the wire (<c>DateOnly</c>);
/// <see cref="PaidAt"/> is a full UTC instant (M6 contract §4).
/// </summary>
public sealed record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    CustomerRefDto Customer,
    StatusRefDto ServiceType,
    StatusRefDto Status,
    DateOnly InvoiceDate,
    decimal Amount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    Guid? ShipmentId,
    string? ShipmentReference,
    bool HasPdf,
    DateTime? PaidAt);

/// <summary>List row + line description, creator, full status history and the paid reference — one call, no second round trip (matches <c>ShipmentDetailDto</c>'s embed-the-children shape).</summary>
public sealed record InvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    CustomerRefDto Customer,
    StatusRefDto ServiceType,
    StatusRefDto Status,
    DateOnly InvoiceDate,
    decimal Amount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    Guid? ShipmentId,
    string? ShipmentReference,
    bool HasPdf,
    DateTime? PaidAt,
    string? LineDescription,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAt,
    string? PaidReference,
    IReadOnlyList<InvoiceStatusHistoryDto> StatusHistory,
    IReadOnlyList<InvoiceLineDto> Lines,
    InvoiceTaxSummaryDto TaxSummary);

/// <summary>
/// Per-status count across the WHOLE filtered set excluding the status filter itself — E7-08's
/// semantics, reused verbatim per M6 contract §3 (zero-count statuses included, ordered by the
/// lookup's <c>sortOrder</c>). <see cref="TotalAmount"/> (N-31/D-72) is the sum of
/// <c>Amount + TaxAmount</c> for that status over the same set the count uses — computed
/// server-side, never stored, single-currency (app-wide INR-only assumption, see
/// <c>money.util.ts</c>). Appended last so the existing positional order is undisturbed.
/// </summary>
public sealed record InvoiceStatusCountDto(
    Guid StatusId,
    string Code,
    string Label,
    int SortOrder,
    int Count,
    decimal TotalAmount);

public sealed record InvoiceListResultDto(
    IReadOnlyList<InvoiceListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<InvoiceStatusCountDto> StatusCounts);

/// <summary>
/// E8-01. Deliberately carries no <c>StatusId</c> — every invoice is created DRAFT
/// server-side (M6 contract §5 names DRAFT as the only starting point; the transition table
/// has no "→ DRAFT" arrow) — and no <c>InvoiceNumber</c>/<c>PdfFilePath</c>, both server-owned
/// (M6 contract §1/§3). <see cref="ShipmentId"/> is nullable — CIF invoices reference a
/// shipment, freight-only invoices stand alone (E8-01); when supplied it must belong to
/// <see cref="CustomerId"/>.
///
/// <c>Amount</c>/<c>TaxAmount</c> were REMOVED from this request in the line-items pass. Both
/// are now derived from <see cref="Lines"/> server-side, so accepting them would be accepting
/// a figure the server is about to overwrite - and, worse, would let a caller state a total
/// that disagrees with the lines backing it on a document that is legally binding once issued.
/// </summary>
public sealed record CreateInvoiceRequest(
    Guid CustomerId,
    Guid? ShipmentId,
    DateOnly InvoiceDate,
    string? LineDescription,
    string Currency,
    IReadOnlyList<UpsertInvoiceLineRequest> Lines);

/// <summary>
/// E8-01/E8-02. Editable ONLY while the invoice is Draft (409 otherwise — M6 contract §2).
/// Deliberately has NO <c>StatusId</c>, <c>InvoiceNumber</c> or <c>PdfFilePath</c> — the two
/// traps M6 contract §2 names explicitly, mirroring D-42/D-43's <c>UpdateShipmentRequest</c>
/// precedent. Status moves only through <c>PUT /invoices/{id}/status</c>.
/// </summary>
public sealed record UpdateInvoiceRequest(
    Guid CustomerId,
    Guid? ShipmentId,
    DateOnly InvoiceDate,
    string? LineDescription,
    string Currency,
    IReadOnlyList<UpsertInvoiceLineRequest> Lines);

/// <summary>
/// E8-02. Any status in the lookup may be targeted; <c>InvoiceService</c> enforces the M6
/// contract §5 transition graph by <c>Code</c>, never by label (D-50 precedent). Deliberately
/// has NO paid marker — the second M6 contract §2 trap; paid moves only through
/// <c>POST /invoices/{id}/mark-paid</c>, which carries its own <c>Invoicing.MarkPaid</c>
/// permission.
/// </summary>
public sealed record ChangeInvoiceStatusRequest(Guid StatusId, string? Note);

/// <summary>E8-07. <see cref="PaidAt"/> defaults to now when omitted; <see cref="PaidReference"/> is optional free text (e.g. a bank transfer note).</summary>
public sealed record MarkInvoicePaidRequest(DateTime? PaidAt, string? PaidReference);

/// <summary>E8-05: customer/status/service-type/date-range filters plus search on invoice number or customer name, and paging. Default <see cref="PageSize"/> is 25 (M6 contract §3).</summary>
public sealed record InvoiceListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? CustomerId,
    Guid? StatusId,
    Guid? ServiceTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate);

/// <summary>
/// One line as it goes OUT. Every money figure here is computed server-side from
/// <see cref="Quantity"/>, <see cref="UnitPrice"/> and <see cref="GstRate"/> via
/// <c>GstCalculator</c> — the client renders them, never derives them, so there is exactly one
/// implementation of the tax arithmetic and the PDF, the screen and the GST return cannot
/// disagree.
/// </summary>
public sealed record InvoiceLineDto(
    Guid Id,
    Guid? InventoryItemId,
    string Description,
    string? HsnCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal? GstRate,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal LineTotal,
    int SortOrder);

/// <summary>
/// One line as it comes IN. <see cref="InventoryItemId"/> is optional — a line may bill
/// something unstocked (freight, handling), which is the freight-only invoice case.
///
/// <see cref="UnitPrice"/>, <see cref="HsnCode"/> and <see cref="GstRate"/> are all nullable
/// so the client can send just an item id and a quantity and let the server fill the rest from
/// the item. A value that IS supplied wins — the operator can override a price or slab per
/// line — but nothing is invented when both the request and the item are silent: the field
/// stays null and the issue-time check refuses, rather than defaulting a tax rate.
/// </summary>
public sealed record UpsertInvoiceLineRequest(
    Guid? InventoryItemId,
    string? Description,
    string? HsnCode,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? GstRate);

/// <summary>
/// The invoice-level tax summary the PDF and the detail screen both render. Derived from the
/// lines, never stored — see <c>Invoice.IsIntraState</c> for why the per-head amounts have no
/// column of their own.
///
/// <see cref="IsIntraState"/> is null on a DRAFT: place of supply is only fixed at issue, and
/// a draft deliberately shows a provisional split rather than claiming a settled one.
/// </summary>
public sealed record InvoiceTaxSummaryDto(
    string? PlaceOfSupplyStateCode,
    string? PlaceOfSupplyStateName,
    bool? IsIntraState,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalTax,
    IReadOnlyList<InvoiceTaxRateBreakdownDto> RateBreakdown);

/// <summary>
/// Taxable value and tax grouped by rate — the "rate-wise summary" a GST invoice carries when
/// its lines span more than one slab. Ordered by rate ascending.
/// </summary>
public sealed record InvoiceTaxRateBreakdownDto(
    decimal GstRate,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount);

/// <summary>Carries the open stream + metadata the authenticated PDF download endpoint needs.</summary>
public sealed record InvoicePdfDownload(Stream Content, string FileName);
