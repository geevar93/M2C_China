using SourcingOps.Domain.Common;

namespace SourcingOps.Domain.Entities;

/// <summary>Configurable category master (FSD §3.3). E.g. "Electronics".</summary>
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Configurable service type lookup. Seed: CIF, FREIGHT_ONLY.</summary>
public class ServiceType : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Configurable lead/customer pipeline stage lookup.</summary>
public class LeadStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Configurable shipment lifecycle lookup.</summary>
public class ShipmentStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Configurable invoice lifecycle lookup. Default: Draft/Issued/Paid/Cancelled.</summary>
public class InvoiceStatus : ILookupEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
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
}
