using SourcingOps.Domain.Common;

namespace SourcingOps.Domain.Entities;

/// <summary>Configurable category master (FSD §3.3). E.g. "Electronics".</summary>
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>See <see cref="Common.ILookupEntity.IsSystemDefault"/> — same retire-only rule (ACTION_PLAN N-8), Category just isn't an <see cref="ILookupEntity"/>.</summary>
    public bool IsSystemDefault { get; set; }
}

/// <summary>Configurable service type lookup. Seed: CIF, FREIGHT_ONLY.</summary>
public class ServiceType : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
}

/// <summary>Configurable lead/customer pipeline stage lookup.</summary>
public class LeadStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
}

/// <summary>Configurable shipment lifecycle lookup.</summary>
public class ShipmentStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
}

/// <summary>Configurable invoice lifecycle lookup. Default: Draft/Issued/Paid/Cancelled.</summary>
public class InvoiceStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
}

/// <summary>
/// Configurable vendor lifecycle lookup (Active/On Hold/Inactive). Not itemized in
/// TECH_SPEC §6 (which lists vendors.status as a plain column) — added as an E1-02
/// deviation so vendor status is FK-backed like every other status column. See
/// SeedDefaults.VendorStatuses.
/// </summary>
public class VendorStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
}

/// <summary>
/// Configurable document type lookup. Not itemized in TECH_SPEC §6 — added for E5-07
/// (FR-VEN-07) following the exact precedent of the D-2 deviation that added
/// <see cref="VendorStatus"/>: the DoD requires every status/category/service-type-shaped
/// value to be an FK to a lookup table, never free text, and a document type is exactly that
/// family (FSD Q5, TECH_SPEC §10 OI-8). See SeedDefaults.VendorDocumentTypes /
/// SeedDefaults.ShipmentDocumentTypes.
///
/// M5 (deviation D-f) extended this single table to serve shipment documents as well, which
/// is why <see cref="Scope"/> exists — see its own doc comment.
/// </summary>
public class DocumentType : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }

    /// <summary>
    /// Which upload path this type belongs to — <see cref="Common.DocumentTypeScopes.Vendor"/>
    /// or <see cref="Common.DocumentTypeScopes.Shipment"/> (deviation D-f).
    ///
    /// Added because M5 needed shipment document types (Packing List / Bill of Lading /
    /// Airway Bill / Invoice) and reusing the same undifferentiated list would make the
    /// vendor-compliance upload dropdown offer "Packing List", and the shipment dropdown
    /// offer "Business Licence" — a visible correctness bug in both directions. Separate
    /// scopes rather than a second lookup table because the two are the same shape and
    /// D-25's generic <c>SeedLookupAsync&lt;TEntity&gt;</c>/<c>MasterDataService</c>
    /// machinery already handles one table cleanly.
    ///
    /// Plain text, deliberately NOT an FK to a further lookup: this is a fixed structural
    /// discriminator naming a code path, not a business-configurable value — a Super Admin
    /// inventing a third scope would have nothing consuming it. That makes it categorically
    /// different from the status/category values FSD §3.3 governs.
    /// </summary>
    public string Scope { get; set; } = Common.DocumentTypeScopes.Vendor;
}
