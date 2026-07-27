namespace SourcingOps.Domain.Entities;

/// <summary>An outbound shipment to a customer (FR-INV-03…09).</summary>
public class Shipment
{
    public Guid Id { get; set; }

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
