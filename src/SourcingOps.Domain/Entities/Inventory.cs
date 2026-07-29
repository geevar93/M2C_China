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

    public ICollection<ShipmentLine> ShipmentLines { get; set; } = new List<ShipmentLine>();
    public ICollection<InventoryInboundEntry> InboundEntries { get; set; } = new List<InventoryInboundEntry>();
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
