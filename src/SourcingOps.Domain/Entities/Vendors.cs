namespace SourcingOps.Domain.Entities;

/// <summary>A sourcing vendor (FR-VEN-01…07).</summary>
public class Vendor
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Region { get; set; }

    public Guid StatusId { get; set; }
    public VendorStatus Status { get; set; } = null!;

    public string? Moq { get; set; }
    public string? LeadTime { get; set; }

    /// <summary>
    /// FR-VEN-06/E5-06 ("MOQ, lead time, payment terms and reliability rating"). Not in
    /// TECH_SPEC §6's `vendors` column list (a spec inconsistency of the same class as the
    /// recorded D-2 for `vendor_statuses`) — added in the M4 pre-migration
    /// `AddVendorPaymentTermsAndM4Indexes`. See the M4 build report.
    /// </summary>
    public string? PaymentTerms { get; set; }

    public decimal? ReliabilityRating { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<VendorCategory> VendorCategories { get; set; } = new List<VendorCategory>();
    public ICollection<CatalogSection> CatalogSections { get; set; } = new List<CatalogSection>();
    public ICollection<VendorDocument> VendorDocuments { get; set; } = new List<VendorDocument>();
}

/// <summary>Many-to-many: category coverage of a vendor.</summary>
public class VendorCategory
{
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

/// <summary>
/// Non-catalog vendor-level document (licence, quality certificate, etc.) — ACTION_PLAN
/// E5-07 / FR-VEN-07, the table DR-4 flagged as missing from TECH_SPEC §6. Filed for
/// reference only per FSD Q5: no version history/`is_latest` (unlike
/// <see cref="CatalogDocument"/>, which E6-03 requires to have it) and no enforcement logic
/// anywhere in the codebase reads <see cref="DocTypeId"/> to gate anything.
/// </summary>
public class VendorDocument
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public Guid DocTypeId { get; set; }
    public DocumentType DocType { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
}
