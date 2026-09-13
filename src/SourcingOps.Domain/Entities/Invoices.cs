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

    /// <summary>
    /// Net taxable value — the sum of every line's <c>Quantity * UnitPrice</c>, GST excluded.
    ///
    /// DERIVED, not user input, since the line-items pass. It stays a stored column rather than
    /// becoming a computed property because the list/summary queries aggregate it in SQL
    /// (<c>GroupBy(...).Sum(i =&gt; i.Amount + i.TaxAmount)</c>) and cannot call into C#.
    /// <c>InvoiceService.RecalculateTotals</c> is the ONLY writer; every mutation of a line
    /// re-runs it, so the column can never drift from the lines it summarises.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Total GST across every line (CGST + SGST, or IGST). Derived exactly like <see cref="Amount"/>.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// The buyer's state code snapshotted at ISSUE time, and the tax treatment that followed
    /// from it. Snapshotted rather than re-read from the customer because a customer who later
    /// moves state must not retroactively change the tax split on an invoice already issued
    /// against the old one.
    ///
    /// Null while the invoice is still a draft — the split is only fixed at issue.
    /// </summary>
    public string? PlaceOfSupplyStateCode { get; set; }

    /// <summary>
    /// True when place of supply equalled the seller's state at issue: CGST + SGST at half the
    /// rate each. False means inter-state — a single IGST line at the full rate. Null while
    /// draft. The per-tax-head amounts are not stored: with a fixed treatment they are exactly
    /// derivable from the lines, and storing them would create a second source of truth.
    /// </summary>
    public bool? IsIntraState { get; set; }

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

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    public ICollection<InvoiceStatusHistory> StatusHistory { get; set; } = new List<InvoiceStatusHistory>();
}

/// <summary>
/// One billable line on an invoice. Replaces the single <c>LineDescription</c> + hand-typed
/// <c>Amount</c>/<c>TaxAmount</c> the invoice carried before: unit price now comes from the
/// item and GST is computed, so neither is a manual entry any more.
///
/// Every tax-determining input is SNAPSHOTTED here rather than read live from the item
/// (<see cref="HsnCode"/>, <see cref="GstRate"/>, <see cref="UnitPrice"/>), on the same
/// reasoning <c>ShipmentLine.UnitCost</c> already documents: a later change to an item's price
/// or slab must never rewrite the basis of an invoice that has been issued. The amounts
/// themselves (taxable value, CGST/SGST/IGST) are deliberately NOT stored — with the rate
/// snapshotted they are deterministic, and a stored copy would be a second source of truth
/// that could drift from it.
/// </summary>
public class InvoiceLine
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>
    /// Nullable: a line may bill something that is not a stocked item (freight, handling,
    /// a service charge), which is exactly the freight-only invoice case. Such a line carries
    /// its own description, SAC code and rate instead.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>Always populated — defaulted from the item's name, then editable.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>HSN (goods) or SAC (services). Required to issue; a draft may still be missing it.</summary>
    public string? HsnCode { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Per-unit price EXCLUSIVE of GST (the basis decision for this pass — tax is added on top, never backed out).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>GST rate as a percent (18m = 18%), snapshotted from the item. Required to issue.</summary>
    public decimal? GstRate { get; set; }

    /// <summary>Stable display order, assigned by the service from the incoming list position.</summary>
    public int SortOrder { get; set; }
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
