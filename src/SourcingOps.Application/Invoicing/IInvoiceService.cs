namespace SourcingOps.Application.Invoicing;

/// <summary>Application service behind <c>InvoicesController</c> (ACTION_PLAN E8-01…E8-08, M6 contract).</summary>
public interface IInvoiceService
{
    Task<InvoiceListResultDto> ListAsync(InvoiceListQuery query, CancellationToken ct = default);

    Task<InvoiceDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Always created Draft; generates the invoice number inside the transaction (M6 contract §1). Throws <c>AppValidationException</c> on bad input.</summary>
    Task<InvoiceDetailDto> CreateAsync(CreateInvoiceRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Editable only in Draft. Throws <c>InvoiceConflictException</c> (409) otherwise. Null when not found.</summary>
    Task<InvoiceDetailDto?> UpdateAsync(Guid id, UpdateInvoiceRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// The only path status ever moves through. DRAFT→ISSUED renders and stores the PDF and
    /// throws <c>AppValidationException</c> if <c>CompanySettings</c> is unconfigured. Any
    /// illegal transition throws <c>InvoiceConflictException</c> (409) naming both codes. Null
    /// when not found.
    /// </summary>
    Task<InvoiceDetailDto?> ChangeStatusAsync(Guid id, ChangeInvoiceStatusRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>ISSUED→PAID only. Throws <c>InvoiceConflictException</c> (409) if the invoice is not currently Issued. Null when not found.</summary>
    Task<InvoiceDetailDto?> MarkPaidAsync(Guid id, MarkInvoicePaidRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null when the invoice does not exist. Throws <c>InvoiceConflictException</c> (409) when no PDF has been generated yet.</summary>
    Task<InvoicePdfDownload?> GetPdfAsync(Guid id, CancellationToken ct = default);
}
