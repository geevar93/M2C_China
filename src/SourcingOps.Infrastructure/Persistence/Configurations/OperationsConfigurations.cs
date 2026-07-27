using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

public class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> b)
    {
        b.ToTable("dispatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Message).IsRequired();
        b.HasIndex(x => x.SentAt);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CatalogDocument).WithMany(d => d.Dispatches).HasForeignKey(x => x.CatalogDocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.StaffUser).WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("inventory_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(300);
        b.Property(x => x.Sku).HasMaxLength(100);
        b.Property(x => x.OnHandQty).HasPrecision(18, 3);
        b.Property(x => x.ReorderThreshold).HasPrecision(18, 3);
        b.HasIndex(x => x.Sku);
        b.HasIndex(x => x.Name);

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(50);
        b.Property(x => x.FreightCost).HasPrecision(18, 2);
        b.Property(x => x.TotalValue).HasPrecision(18, 2);
        b.HasIndex(x => x.DispatchDate);
        b.HasIndex(x => x.CreatedAt);

        // Nullable + partial unique index (not NOT NULL + plain unique) — see Shipment.Reference's
        // doc comment: the table is empty until E7-05, so uniqueness only needs to hold once
        // rows actually carry a value.
        b.HasIndex(x => x.Reference).IsUnique().HasFilter("reference IS NOT NULL");

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> b)
    {
        b.ToTable("shipment_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 3);

        b.HasOne(x => x.Shipment).WithMany(s => s.Lines).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.InventoryItem).WithMany(i => i.ShipmentLines).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShipmentDocumentConfiguration : IEntityTypeConfiguration<ShipmentDocument>
{
    public void Configure(EntityTypeBuilder<ShipmentDocument> b)
    {
        b.ToTable("shipment_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FilePath).IsRequired();
        b.Property(x => x.OriginalFilename).IsRequired().HasMaxLength(500);
        b.Property(x => x.DocType).IsRequired().HasMaxLength(50);

        b.HasOne(x => x.Shipment).WithMany(s => s.Documents).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(x => x.Id);
        b.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(50);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.TaxAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.HasIndex(x => x.InvoiceNumber).IsUnique();
        b.HasIndex(x => x.InvoiceDate);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Shipment).WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).IsRequired().HasMaxLength(100);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
        b.Property(x => x.DetailsJson).HasColumnName("details").HasColumnType("jsonb");
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => x.EntityType);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
