namespace SourcingOps.Domain.Entities;

/// <summary>A titled grouping of catalog PDFs under a vendor (FR-CAT-01…08).</summary>
public class CatalogSection
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>Free-form tags (FR-CAT-07 [C]) — Postgres text[].</summary>
    public string[] Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; }

    public ICollection<CatalogDocument> Documents { get; set; } = new List<CatalogDocument>();
}

/// <summary>A versioned PDF document within a catalog section (FR-CAT-02, FR-CAT-03).</summary>
public class CatalogDocument
{
    public Guid Id { get; set; }

    public Guid CatalogSectionId { get; set; }
    public CatalogSection CatalogSection { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? VersionLabel { get; set; }
    public bool IsLatest { get; set; } = true;

    public Guid UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;
    public DateTime UploadedAt { get; set; }

    public ICollection<Dispatch> Dispatches { get; set; } = new List<Dispatch>();
}
