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
    IReadOnlyList<InvoiceStatusHistoryDto> StatusHistory);

/// <summary>
/// Per-status count across the WHOLE filtered set excluding the status filter itself — E7-08's
/// semantics, reused verbatim per M6 contract §3 (zero-count statuses included, ordered by the
/// lookup's <c>sortOrder</c>).
/// </summary>
public sealed record InvoiceStatusCountDto(
    Guid StatusId,
    string Code,
    string Label,
    int SortOrder,
    int Count);

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
/// </summary>
public sealed record CreateInvoiceRequest(
    Guid CustomerId,
    Guid? ShipmentId,
    DateOnly InvoiceDate,
    string? LineDescription,
    decimal Amount,
    decimal TaxAmount,
    string Currency);

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
    decimal Amount,
    decimal TaxAmount,
    string Currency);

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

/// <summary>Carries the open stream + metadata the authenticated PDF download endpoint needs.</summary>
public sealed record InvoicePdfDownload(Stream Content, string FileName);
