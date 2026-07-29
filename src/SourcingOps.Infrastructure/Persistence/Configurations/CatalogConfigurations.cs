using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

public class CatalogSectionConfiguration : IEntityTypeConfiguration<CatalogSection>
{
    public void Configure(EntityTypeBuilder<CatalogSection> b)
    {
        b.ToTable("catalog_sections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Tags).HasColumnType("text[]");
        b.HasIndex(x => x.Title);

        // E6-07/DoD: ?tag= is an array-containment filter (GET /catalog-sections) — a btree
        // index (the default) cannot serve `Contains`; GIN is required, same precedent as
        // CustomerConfiguration.Tags. Added in the M4 pre-migration
        // AddVendorPaymentTermsAndM4Indexes.
        b.HasIndex(x => x.Tags).HasMethod("gin");

        b.HasOne(x => x.Vendor).WithMany(v => v.CatalogSections).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CatalogDocumentConfiguration : IEntityTypeConfiguration<CatalogDocument>
{
    public void Configure(EntityTypeBuilder<CatalogDocument> b)
    {
        b.ToTable("catalog_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FilePath).IsRequired();
        b.Property(x => x.OriginalFilename).IsRequired().HasMaxLength(500);
        b.HasIndex(x => x.UploadedAt);

        // E6-03/DoD: replaces the earlier standalone IsLatest index — "documents for this
        // section that are latest" (the version-list/download-latest query shape) is what
        // gets queried, not "every latest document platform-wide", so the composite serves
        // the real access pattern and CatalogSectionId no longer needs a second single-column
        // index of its own. Added in the M4 pre-migration AddVendorPaymentTermsAndM4Indexes.
        b.HasIndex(x => new { x.CatalogSectionId, x.IsLatest });

        b.HasOne(x => x.CatalogSection).WithMany(s => s.Documents).HasForeignKey(x => x.CatalogSectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
