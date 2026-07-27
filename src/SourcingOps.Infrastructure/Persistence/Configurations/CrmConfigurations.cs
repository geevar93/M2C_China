using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).IsRequired().HasMaxLength(32);
        b.Property(x => x.Tags).HasColumnType("text[]");
        b.HasIndex(x => x.Phone); // FR-CRM-09 duplicate detection
        b.HasIndex(x => x.CreatedAt);

        // E4-06/DoD: ServiceTypeId/StatusId/OwnerUserId already get an index automatically
        // from EF Core's FK-relationship configuration below (verified in the InitialCreate
        // migration: ix_customers_service_type_id/status_id/owner_user_id). Region and Tags
        // are the two E4-06 filter columns that do NOT come from a relationship, so they need
        // an explicit index — added here as the pre-M3 migration did not anticipate list
        // filtering yet. See AddCustomerListFilterIndexes.
        b.HasIndex(x => x.Region);
        b.HasIndex(x => x.Tags).HasMethod("gin"); // array-containment filter (?tag=)

        // External-purchase detail (freight-only customers, FSD Q1) — all six nullable,
        // schema only this pass (pre-M3 migration, see ACTION_PLAN §10.4).
        b.Property(x => x.ExternalMarketplace).HasMaxLength(200);
        b.Property(x => x.ExternalOrderRef).HasMaxLength(200);
        b.Property(x => x.ExternalSupplierName).HasMaxLength(300);
        b.Property(x => x.ExternalOrderValue).HasPrecision(18, 2);
        b.Property(x => x.ExternalOrderCurrency).HasMaxLength(3);

        b.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class CustomerCategoryConfiguration : IEntityTypeConfiguration<CustomerCategory>
{
    public void Configure(EntityTypeBuilder<CustomerCategory> b)
    {
        b.ToTable("customer_categories");
        b.HasKey(x => new { x.CustomerId, x.CategoryId });

        b.HasOne(x => x.Customer).WithMany(c => c.CustomerCategories).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> b)
    {
        b.ToTable("interactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).IsRequired().HasMaxLength(50);
        b.Property(x => x.Text).IsRequired();
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.FollowUpDate);

        b.HasOne(x => x.Customer).WithMany(c => c.Interactions).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
