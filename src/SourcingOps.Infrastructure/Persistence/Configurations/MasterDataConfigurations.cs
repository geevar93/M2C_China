using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.IsActive);
    }
}

public class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> b)
    {
        b.ToTable("service_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class LeadStatusConfiguration : IEntityTypeConfiguration<LeadStatus>
{
    public void Configure(EntityTypeBuilder<LeadStatus> b)
    {
        b.ToTable("lead_statuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class ShipmentStatusConfiguration : IEntityTypeConfiguration<ShipmentStatus>
{
    public void Configure(EntityTypeBuilder<ShipmentStatus> b)
    {
        b.ToTable("shipment_statuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class InvoiceStatusConfiguration : IEntityTypeConfiguration<InvoiceStatus>
{
    public void Configure(EntityTypeBuilder<InvoiceStatus> b)
    {
        b.ToTable("invoice_statuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class VendorStatusConfiguration : IEntityTypeConfiguration<VendorStatus>
{
    public void Configure(EntityTypeBuilder<VendorStatus> b)
    {
        b.ToTable("vendor_statuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}
