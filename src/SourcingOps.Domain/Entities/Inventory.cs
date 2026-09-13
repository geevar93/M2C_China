namespace SourcingOps.Domain.Entities;

/// <summary>A stocked item (FR-INV-01…09).</summary>
public class InventoryItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }

    /// <summary>
    /// E7-01's acceptance criteria name "name/description", but TECH_SPEC §6's
    /// <c>inventory_items</c> row omits a description column entirely. Added in the M5 pass
    /// as an explicit deviation, following the exact precedent of D-19 (<c>vendors.payment_terms</c>,
    /// named by FR-VEN-06 and likewise absent from §6). Optional — the approved inventory
    /// screen does not render it, so it is a detail/edit field only.
    /// </summary>
    public string? Description { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid? VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    public string Unit { get; set; } = "pcs";
    public decimal OnHandQty { get; set; }
    public decimal ReorderThreshold { get; set; }

    /// <summary>
    /// Per-unit cost, nullable. Added in the M5 pass (deviation D-a): the approved inventory
    /// screen renders a per-row "Stock Value" column and an "On-Hand Value" stat tile, and no
    /// cost column exists anywhere in TECH_SPEC §6's schema — the screen is unportable without
    /// it. Stock value is always COMPUTED (<c>OnHandQty * UnitCost</c>) and never stored, so
    /// there is exactly one source of truth for it.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Per-unit SELLING price, exclusive of GST — what a customer is billed, as distinct from
    /// <see cref="UnitCost"/>, which is what the item cost to acquire. Nullable: not every
    /// stocked item is sold at a list price.
    ///
    /// These two must never be conflated. Invoicing defaults a line's unit price from this
    /// field and NEVER falls back to <see cref="UnitCost"/> — silently billing a customer at
    /// cost would under-charge by the entire margin, and would do it invisibly, on a document
    /// that is legally binding once issued.
    /// </summary>
    public decimal? SellingPrice { get; set; }

    /// <summary>
    /// HSN (goods) or SAC (services) code. Nullable on the item, but an invoice line without
    /// one cannot be issued — a compliant tax invoice must state it per line.
    /// </summary>
    public string? HsnCode { get; set; }

    /// <summary>
    /// GST rate as a PERCENT, not a fraction — <c>18m</c> means 18%, matching how the statutory
    /// slabs (0/5/12/18/28) are written and read. Stored per item because a mixed basket
    /// legitimately spans slabs. Snapshotted onto the invoice line at creation so a later rate
    /// change never rewrites the basis of an invoice that has already been issued (the same
    /// reasoning <see cref="ShipmentLine.UnitCost"/> documents for its own snapshot).
    /// </summary>
    public decimal? GstRate { get; set; }

    public ICollection<ShipmentLine> ShipmentLines { get; set; } = new List<ShipmentLine>();
    public ICollection<InventoryInboundEntry> InboundEntries { get; set; } = new List<InventoryInboundEntry>();
    public ICollection<InventoryStockAdjustment> StockAdjustments { get; set; } = new List<InventoryStockAdjustment>();
}

/// <summary>
/// A recorded inbound stock receipt (E7-02 / FR-INV-02). Added in the M5 pass (deviation
/// D-d): E7-02's criteria require "an inbound entry <b>with date and reference</b>", which is
/// a durable, queryable business record. The audit log is a cross-cutting concern keyed by
/// action string with a free-form JSON detail blob — deliberately not a queryable business
/// table, so it cannot be the only home for this.
///
/// Recording an entry increments <see cref="InventoryItem.OnHandQty"/> in the same
/// transaction and is additionally audit-logged; the two serve different readers.
/// </summary>
public class InventoryInboundEntry
{
    public Guid Id { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>The business date of the receipt, distinct from <see cref="CreatedAt"/> (when it was keyed in).</summary>
    public DateOnly EntryDate { get; set; }

    /// <summary>Free-text supplier/GRN reference, optional per E7-02's wording.</summary>
    public string? Reference { get; set; }

    public Guid RecordedByUserId { get; set; }
    public User RecordedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A recorded physical-count correction (N-38). D-42 already made deliberate that
/// <see cref="InventoryItem.OnHandQty"/> moves only through <see cref="InventoryInboundEntry"/>
/// and shipment lines — a stock-take discrepancy had no way to correct it. This is NOT an
/// editable quantity field: it is the same durable, queryable business record shape as
/// <see cref="InventoryInboundEntry"/> (see that type's doc comment), so the fact a count ever
/// disagreed with the system stays visible rather than being silently overwritten.
///
/// <see cref="PreviousQty"/> and <see cref="Delta"/> are STORED, not recomputed. Unlike stock
/// value elsewhere in this module (always computed from current state), the point of this
/// record is what the numbers were AT THAT MOMENT — recomputing later from whatever
/// <see cref="InventoryItem.OnHandQty"/> happens to be now would defeat the audit purpose.
///
/// Recording an adjustment sets <see cref="InventoryItem.OnHandQty"/> to
/// <see cref="CountedQty"/> in the same transaction and is additionally audit-logged, exactly
/// as <see cref="InventoryInboundEntry"/> does for inbound receipts.
/// </summary>
public class InventoryStockAdjustment
{
    public Guid Id { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>What the human physically counted. Never negative — a count of nothing is zero, not less.</summary>
    public decimal CountedQty { get; set; }

    /// <summary>Snapshot of <see cref="InventoryItem.OnHandQty"/> immediately before this adjustment applied. Can be negative (D-35 oversold).</summary>
    public decimal PreviousQty { get; set; }

    /// <summary>Stored, not computed: <c>CountedQty - PreviousQty</c> at the moment this was recorded. May be zero — "we counted and it was correct" is still a fact worth keeping.</summary>
    public decimal Delta { get; set; }

    /// <summary>Free text, required — a correction with no stated reason is exactly the audit hole this feature closes.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The business date the count happened, distinct from <see cref="AdjustedAt"/> (when it was keyed in).</summary>
    public DateOnly AdjustedOn { get; set; }

    public DateTime AdjustedAt { get; set; }

    public Guid AdjustedByUserId { get; set; }
    public User AdjustedByUser { get; set; } = null!;
}
