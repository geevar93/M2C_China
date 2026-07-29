using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

public class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("vendors");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.ReliabilityRating).HasPrecision(3, 2);
        b.HasIndex(x => x.Name);

        // E5-04/DoD: region is a GET /vendors filter column with no relationship to piggy-back
        // an FK index on (unlike StatusId below), so it needs an explicit index — same
        // reasoning as CustomerConfiguration.Region (AddCustomerListFilterIndexes). Added in
        // the M4 pre-migration AddVendorPaymentTermsAndM4Indexes.
        b.HasIndex(x => x.Region);

        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VendorCategoryConfiguration : IEntityTypeConfiguration<VendorCategory>
{
    public void Configure(EntityTypeBuilder<VendorCategory> b)
    {
        b.ToTable("vendor_categories");
        b.HasKey(x => new { x.VendorId, x.CategoryId });

        b.HasOne(x => x.Vendor).WithMany(v => v.VendorCategories).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// ACTION_PLAN E5-07 / FR-VEN-07 (DR-4's flagged gap). Mirrors <c>CatalogDocumentConfiguration</c>
/// closely, minus <c>IsLatest</c>/versioning — out of scope per FSD Q5 ("filed for reference
/// only"). <see cref="VendorDocument.VendorId"/> and <see cref="VendorDocument.DocTypeId"/> each
/// get EF Core's automatic FK index, which already serves the two queries actually made
/// ("documents for this vendor", E3-08's "is this doc type referenced" check) — no extra
/// composite index is needed the way catalog_documents needed one for its is_latest filter.
/// </summary>
public class VendorDocumentConfiguration : IEntityTypeConfiguration<VendorDocument>
{
    public void Configure(EntityTypeBuilder<VendorDocument> b)
    {
        b.ToTable("vendor_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FilePath).IsRequired();
        b.Property(x => x.OriginalFilename).IsRequired().HasMaxLength(500);
        b.HasIndex(x => x.UploadedAt);

        b.HasOne(x => x.Vendor).WithMany(v => v.VendorDocuments).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.DocType).WithMany().HasForeignKey(x => x.DocTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
