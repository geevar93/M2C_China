namespace SourcingOps.Application.Interfaces;

/// <summary>
/// Only ever populated when ALL THREE of AccountName/AccountNumber/Ifsc are present on
/// <c>CompanySettings</c> (M6 contract §0) — a partial bank block on a financial document is
/// worse than none, so <c>InvoiceService</c> never constructs one otherwise.
/// </summary>
public sealed record InvoicePdfBankDetails(string AccountName, string AccountNumber, string Ifsc, string? Branch);

/// <summary>
/// Everything <see cref="IInvoicePdfRenderer"/> needs to lay out one invoice PDF.
/// <see cref="LegalEntityName"/> and <see cref="RegisteredAddress"/> are non-nullable here
/// deliberately: <c>InvoiceService</c> owns the "fail loudly when a required company field is
/// still null" check (M6 contract §0, <c>CompanySettings</c>'s own doc comment) and never
/// constructs this model until that check has passed — the renderer trusts its input and
/// performs no business validation of its own.
/// </summary>
public sealed record InvoicePdfModel(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string CustomerName,
    string? CustomerCity,
    string? CustomerRegion,
    string? CustomerGstin,
    string? LineDescription,
    decimal Amount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    string? ShipmentReference,
    string LegalEntityName,
    string RegisteredAddress,
    string? Gstin,
    InvoicePdfBankDetails? BankDetails,
    string? DeclarationText,
    IReadOnlyList<InvoicePdfLine> Lines,
    InvoicePdfTaxSummary TaxSummary);

/// <summary>
/// One rendered line. Every money figure arrives ALREADY COMPUTED by <c>GstCalculator</c> —
/// the renderer does no arithmetic of its own, so the PDF cannot disagree with the screen or
/// the stored totals. Same trust-your-input rule the model doc states for the company fields.
/// </summary>
public sealed record InvoicePdfLine(
    string Description,
    string? HsnCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal GstRate,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal LineTotal);

/// <summary>
/// The invoice-level tax block. <see cref="IsIntraState"/> decides which heads are printed:
/// CGST + SGST columns for an intra-state supply, a single IGST column otherwise. Printing all
/// three with zeroes in two of them is how a reader loses confidence in a tax document, so the
/// renderer shows only the heads that actually apply.
/// </summary>
public sealed record InvoicePdfTaxSummary(
    string? PlaceOfSupplyStateCode,
    string? PlaceOfSupplyStateName,
    bool IsIntraState,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalTax);

/// <summary>
/// PDF rendering seam (DR-3). The QuestPDF implementation lives in <c>Infrastructure</c> —
/// <c>Application</c> must not reference QuestPDF (TECH_SPEC §4.1's narrow-seam rule, same
/// reasoning as E9-09's <c>IDispatchMessageSender</c>).
/// </summary>
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfModel model);
}
