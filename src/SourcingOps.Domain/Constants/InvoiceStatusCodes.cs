namespace SourcingOps.Domain.Constants;

/// <summary>
/// M6 contract §5 / deviation D-50 precedent: <c>InvoiceService</c> branches on the invoice
/// status lookup's <c>Code</c>, never its Super-Admin-editable <c>Label</c>. Named constants
/// here, mirroring <c>SeedDefaults.InvoiceStatuses</c>' codes exactly, so a status-lifecycle
/// check is never a bare string literal at the call site.
/// </summary>
public static class InvoiceStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Issued = "ISSUED";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
}
