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
