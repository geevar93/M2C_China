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
}

/// <summary>Many-to-many: category coverage of a vendor.</summary>
public class VendorCategory
{
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
