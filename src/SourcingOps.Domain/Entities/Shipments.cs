namespace SourcingOps.Domain.Entities;

/// <summary>An outbound shipment to a customer (FR-INV-03…09).</summary>
public class Shipment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable shipment ID the approved prototype shows as the headline identifier —
    /// e.g. "SHP-2607-014" (prefix + YYMM + zero-padded sequence), per TECH_SPEC §6.
    /// Uniqueness is enforced over non-null values by a partial unique index (see
    /// ShipmentConfiguration). Populated server-side from M5 onward (E7-05, deviation D-i):
    /// <c>ShipmentService</c> generates it inside the creating transaction and retries on a
    /// 23505 unique violation, and never accepts a client-supplied value. The column stays
    /// nullable because the partial index already carries the constraint and making it
    /// NOT NULL would be a second migration for no behavioural gain.
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

    /// <summary>
    /// Server-computed as the sum of <c>Quantity * UnitCost</c> over <see cref="Lines"/>
    /// whenever the shipment HAS lines, and accepted from the caller only when it has none —
    /// the freight-only case (deviation D-c). A client-supplied total that contradicts the
    /// lines is never trusted.
    /// </summary>
    public decimal? TotalValue { get; set; }

    public string? Mode { get; set; }
    public string? AwbOrBl { get; set; }
    public DateTime? Eta { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<ShipmentLine> Lines { get; set; } = new List<ShipmentLine>();
    public ICollection<ShipmentDocument> Documents { get; set; } = new List<ShipmentDocument>();
    public ICollection<ShipmentStatusHistory> StatusHistory { get; set; } = new List<ShipmentStatusHistory>();
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

    /// <summary>
    /// Snapshotted from <see cref="InventoryItem.UnitCost"/> at line creation, or taken from
    /// the caller when supplied (deviation D-b). Snapshotted rather than read live because
    /// E8 (invoicing, M6) raises CIF invoices AGAINST a shipment — if a later item price
    /// change silently rewrote historical shipment values it would also rewrite the basis of
    /// an already-issued invoice.
    /// </summary>
    public decimal? UnitCost { get; set; }
}

/// <summary>
/// A timestamped shipment status transition (E7-07 / FR-INV-05). Added in the M5 pass
/// (deviation D-e): the approved shipment detail screen renders a 4-step progress stepper
/// with a <c>when</c> timestamp under each step, which is unrenderable without stored
/// transition times. E7-07 requires transitions be "audit-logged <b>and timestamped</b>" —
/// both are written, because they serve different readers: <c>audit_logs</c> is the
/// cross-entity compliance trail, this table is the shipment's own displayable history.
///
/// A row is seeded at shipment creation for the opening status, so the stepper always has a
/// first entry.
/// </summary>
public class ShipmentStatusHistory
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public Guid StatusId { get; set; }
    public ShipmentStatus Status { get; set; } = null!;

    public Guid ChangedByUserId { get; set; }
    public User ChangedBy { get; set; } = null!;

    public DateTime ChangedAt { get; set; }

    public string? Note { get; set; }
}

/// <summary>A reference document (packing list / AWB / BL scan) attached to a shipment.</summary>
public class ShipmentDocument
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>
    /// FK into the <c>document_types</c> lookup, scoped to <see cref="Common.DocumentTypeScopes.Shipment"/>.
    ///
    /// SUPERSEDES this property's previous form (a plain <c>DocType</c> text column) and the
    /// comment that defended it. That comment argued a shipment document type is "not one of
    /// the FSD's configurable master-data categories" and leaned on TECH_SPEC §6 naming the
    /// column <c>doc_type</c> without an <c>_id</c> suffix. Both arguments are now overruled:
    /// the §7 DoD requires every category-shaped value to be an FK to a lookup table and never
    /// free text (FSD §3.3), and D-25 already created <c>document_types</c> for vendor
    /// documents on exactly that reasoning. §6's column naming is not evidence of intent — it
    /// listed <c>vendors.status</c> as a plain column too, which D-2 corrected the same way.
    /// See deviation D-f.
    /// </summary>
    public Guid DocumentTypeId { get; set; }
    public DocumentType DocumentType { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;

    public DateTime UploadedAt { get; set; }
}
