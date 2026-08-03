namespace SourcingOps.Application.Common;

/// <summary>
/// One 409 exception type covering every invoicing state conflict (M6 contract §2/§5): editing
/// an invoice that is no longer Draft, an illegal status transition (including a mark-paid
/// attempt against a non-Issued invoice), and a PDF download requested before DRAFT→ISSUED has
/// ever run. A single type rather than one class per case, unlike
/// <c>InsufficientStockException</c>, because every case here is "the request is well-formed
/// but the invoice's current state forbids it" — the same underlying shape — and
/// <see cref="Extensions"/> already lets each call site name its own conflicting facts (e.g.
/// <c>fromStatus</c>/<c>toStatus</c> for a bad transition) without a new exception type per case.
/// </summary>
public sealed class InvoiceConflictException : Exception
{
    public string Title { get; }
    public IReadOnlyDictionary<string, object?> Extensions { get; }

    public InvoiceConflictException(string title, string detail, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail)
    {
        Title = title;
        Extensions = extensions ?? new Dictionary<string, object?>();
    }
}
