namespace SourcingOps.Domain.Entities;

/// <summary>An outbound shipment to a customer (FR-INV-03…09).</summary>
public class Shipment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable shipment ID the approved prototype shows as the headline identifier —
    /// e.g. "SHP-2607-014" (prefix + YYMM + zero-padded sequence), per TECH_SPEC §6.
    /// Nullable with a unique index (see ShipmentConfiguration), NOT NOT-NULL: the table is
    /// empty today (no ShipmentsController exists until E7-05, M5 scope), so generating
    /// references for hypothetical rows would be meaningless busywork now, and a NOT NULL
    /// column would force a throwaway value E7-05 would just overwrite. Uniqueness is
    /// enforced only over non-null values via a partial index, so this stays enforceable the
    /// moment E7-05 starts populating it without a further migration.
    /// </summary>
    public string? Reference { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string? Destination { get; set; }

    public Guid ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;

    public DateTime? DispatchDate { get; set; }

    public Guid StatusId { get; set; }
    public ShipmentStatus Status { get; set; } = null!;

    public decimal? FreightCost { get; set; }
    public decimal? TotalValue { get; set; }
    public string? Mode { get; set; }
    public string? AwbOrBl { get; set; }
    public DateTime? Eta { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<ShipmentLine> Lines { get; set; } = new List<ShipmentLine>();
    public ICollection<ShipmentDocument> Documents { get; set; } = new List<ShipmentDocument>();
}

/// <summary>A line item on a shipment referencing an inventory item and quantity.</summary>
public class ShipmentLine
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public decimal Quantity { get; set; }
}

/// <summary>A reference document (packing list / AWB / BL scan) attached to a shipment.</summary>
public class ShipmentDocument
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>
    /// E.g. "PackingList"/"BillOfLading"/"Invoice". Kept as plain text, not a lookup —
    /// not one of the FSD's configurable master-data categories and TECH_SPEC §6 names
    /// it "doc_type" without an "_id" FK suffix, unlike every genuine status/category
    /// column in the schema.
    /// </summary>
    public string DocType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}
