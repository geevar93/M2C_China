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

    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string? LineDescription { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Stored as INR now; column future-proofs multi-currency (FSD A7).</summary>
    public string Currency { get; set; } = "INR";

    public Guid StatusId { get; set; }
    public InvoiceStatus Status { get; set; } = null!;

    public string? PdfFilePath { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
