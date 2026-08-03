namespace SourcingOps.Domain.Entities;

/// <summary>Lightweight document-based invoice (FSD §6.8, FR-BIL-01…07). No GST engine, no payment gateway.</summary>
public class Invoice
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Nullable — CIF invoices reference a shipment; freight-only invoices stand alone.</summary>
    public Guid? ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    /// <summary>
    /// Server-generated <c>{prefix}-{yyMM}-{NNN}</c> (M6/E8-08). Reuses
    /// <c>ShipmentService</c>'s <c>SHP-YYMM-NNN</c> pattern verbatim (M6 contract §1): generated
    /// inside the creating transaction and retried on a 23505 unique violation against the
    /// unique index on this column. Never accepted from the caller.
    /// </summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Logical date, no time component — serialised as a bare date string (M6 contract §4).</summary>
    public DateTime InvoiceDate { get; set; }
    public string? LineDescription { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Stored as INR now; column future-proofs multi-currency (FSD A7).</summary>
    public string Currency { get; set; } = "INR";

    public Guid StatusId { get; set; }
    public InvoiceStatus Status { get; set; } = null!;

    /// <summary>Set only when the DRAFT→ISSUED transition renders and stores the PDF (M6 contract §5). Never exposed in a DTO — see InvoiceListItemDto.HasPdf.</summary>
    public string? PdfFilePath { get; set; }

    /// <summary>Set only by ISSUED→PAID via <c>POST /invoices/{id}/mark-paid</c> (E8-07). Null while unpaid.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Optional free-text reference (e.g. a bank transfer note) captured when marking paid (E8-07). Null while unpaid or when none was given.</summary>
    public string? PaidReference { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public ICollection<InvoiceStatusHistory> StatusHistory { get; set; } = new List<InvoiceStatusHistory>();
}

/// <summary>
/// A timestamped invoice status transition (E8-02, M6 contract §5/§8-D-65). Mirrors
/// <see cref="ShipmentStatusHistory"/> exactly, including "a row is seeded at creation so the
/// detail screen's history never starts empty" (same reasoning as deviation D-e).
/// </summary>
public class InvoiceStatusHistory
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public Guid StatusId { get; set; }
    public InvoiceStatus Status { get; set; } = null!;

    public Guid ChangedByUserId { get; set; }
    public User ChangedBy { get; set; } = null!;

    public DateTime ChangedAt { get; set; }

    public string? Note { get; set; }
}
