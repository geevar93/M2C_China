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
