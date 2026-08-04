using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

/// <summary>
/// M6/E8-06 pass: <c>CatalogDocumentId</c> went nullable and <c>InvoiceId</c> was added — see
/// <see cref="Dispatch"/>'s doc comment. The CHECK constraint (not just two nullable FKs, which
/// only prevents a NOT NULL violation) is what actually enforces "exactly one of the two",
/// following the same database-enforced-invariant precedent as
/// <c>CompanySettingsConfiguration</c>'s singleton CHECK — see
/// feedback-efcore-npgsql-gotchas memory: HasCheckConstraint goes inside ToTable(), not as a
/// separate call.
/// </summary>
public class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> b)
    {
        b.ToTable("dispatches", t => t.HasCheckConstraint(
            "ck_dispatches_exactly_one_target",
            "(catalog_document_id IS NOT NULL AND invoice_id IS NULL) OR (catalog_document_id IS NULL AND invoice_id IS NOT NULL)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Message).IsRequired();
        b.HasIndex(x => x.SentAt);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CatalogDocument).WithMany(d => d.Dispatches).HasForeignKey(x => x.CatalogDocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
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
        b.Property(x => x.UnitCost).HasPrecision(18, 2); // D-a
        b.HasIndex(x => x.Sku);
        b.HasIndex(x => x.Name);

        // E7-03/DoD: the stock-level filter (`low` = OnHandQty < ReorderThreshold, `healthy` =
        // the complement) is a two-column comparison, which no single-column btree index can
        // serve as a range scan. Indexing OnHandQty is still worth it: it serves the
        // `stockLevel=all` ordering fallback and the D-k summary's negative-stock count
        // (OnHandQty < 0), which IS a single-column predicate. CategoryId/VendorId already
        // get EF Core's automatic FK indexes, which serve the other two named filters.
        b.HasIndex(x => x.OnHandQty);

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// E7-02 / deviation D-d. <see cref="InventoryInboundEntry.InventoryItemId"/> gets EF Core's
/// automatic FK index, which serves the only query made ("entries for this item, newest
/// first") once paired with the explicit <c>EntryDate</c> index below.
/// </summary>
public class InventoryInboundEntryConfiguration : IEntityTypeConfiguration<InventoryInboundEntry>
{
    public void Configure(EntityTypeBuilder<InventoryInboundEntry> b)
    {
        b.ToTable("inventory_inbound_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Reference).HasMaxLength(200);

        // DateOnly -> Postgres `date`, matching D-d's stated column type. EntryDate is the
        // business date of the receipt; CreatedAt is when it was keyed in. They differ
        // whenever stock is backdated, which is why both are stored.
        b.Property(x => x.EntryDate).HasColumnType("date");

        b.HasIndex(x => new { x.InventoryItemId, x.EntryDate });

        b.HasOne(x => x.InventoryItem).WithMany(i => i.InboundEntries).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.RecordedBy).WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// N-38. Mirrors <see cref="InventoryInboundEntryConfiguration"/> exactly: the FK index EF
/// Core creates automatically on <see cref="InventoryStockAdjustment.InventoryItemId"/> serves
/// the only query made ("this item's adjustment history, newest first") once paired with the
/// explicit AdjustedOn index below.
/// </summary>
public class InventoryStockAdjustmentConfiguration : IEntityTypeConfiguration<InventoryStockAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryStockAdjustment> b)
    {
        b.ToTable("inventory_stock_adjustments");
        b.HasKey(x => x.Id);
        b.Property(x => x.CountedQty).HasPrecision(18, 3);
        b.Property(x => x.PreviousQty).HasPrecision(18, 3);
        b.Property(x => x.Delta).HasPrecision(18, 3);
        b.Property(x => x.Reason).IsRequired().HasMaxLength(500);

        // DateOnly -> Postgres `date`, matching InventoryInboundEntryConfiguration's EntryDate.
        // AdjustedOn is the business date of the count; AdjustedAt is when it was keyed in.
        b.Property(x => x.AdjustedOn).HasColumnType("date");

        b.HasIndex(x => new { x.InventoryItemId, x.AdjustedOn });

        b.HasOne(x => x.InventoryItem).WithMany(i => i.StockAdjustments).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AdjustedByUser).WithMany().HasForeignKey(x => x.AdjustedByUserId).OnDelete(DeleteBehavior.Restrict);
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

        // E7-08/DoD: the shipments screen's primary access pattern is "one status tab, most
        // recent dispatch date first" — an equality on status_id plus an ordered range on
        // dispatch_date. This composite serves the whole of that in one scan, same reasoning
        // as D-20's (catalog_section_id, is_latest) composite.
        //
        // It SUPERSEDES the standalone ix_shipments_status_id, which the M5 migration
        // therefore drops: a btree index on (status_id, dispatch_date) already serves any
        // status_id-only predicate as a leading-column prefix, so keeping both would cost
        // write throughput for nothing. E3-08's "is this status referenced" check still
        // resolves through this index. ix_shipments_dispatch_date is kept, because a
        // date-range query with NO status filter — which is exactly what the "All" tab
        // issues — cannot use a composite whose leading column it does not constrain.
        b.HasIndex(x => new { x.StatusId, x.DispatchDate });

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
        b.Property(x => x.UnitCost).HasPrecision(18, 2); // D-b: snapshotted at line creation

        b.HasOne(x => x.Shipment).WithMany(s => s.Lines).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.InventoryItem).WithMany(i => i.ShipmentLines).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>E7-07 / deviation D-e. Composite index serves the only query: "this shipment's history, newest first".</summary>
public class ShipmentStatusHistoryConfiguration : IEntityTypeConfiguration<ShipmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<ShipmentStatusHistory> b)
    {
        b.ToTable("shipment_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.ShipmentId, x.ChangedAt });

        b.HasOne(x => x.Shipment).WithMany(s => s.StatusHistory).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// E7-09 / FR-INV-08. Mirrors <c>VendorDocumentConfiguration</c> exactly, including the
/// <c>DocumentTypeId</c> FK that replaced the old free-text <c>doc_type</c> column (D-f).
/// </summary>
public class ShipmentDocumentConfiguration : IEntityTypeConfiguration<ShipmentDocument>
{
    public void Configure(EntityTypeBuilder<ShipmentDocument> b)
    {
        b.ToTable("shipment_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FilePath).IsRequired();
        b.Property(x => x.OriginalFilename).IsRequired().HasMaxLength(500);
        b.HasIndex(x => x.UploadedAt);

        b.HasOne(x => x.Shipment).WithMany(s => s.Documents).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
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
        b.Property(x => x.PaidReference).HasMaxLength(300); // M6/E8-07
        b.HasIndex(x => x.InvoiceNumber).IsUnique();
        b.HasIndex(x => x.InvoiceDate);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Shipment).WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>M6/E8-02. Mirrors <see cref="ShipmentStatusHistoryConfiguration"/> exactly — composite index serves "this invoice's history, newest first".</summary>
public class InvoiceStatusHistoryConfiguration : IEntityTypeConfiguration<InvoiceStatusHistory>
{
    public void Configure(EntityTypeBuilder<InvoiceStatusHistory> b)
    {
        b.ToTable("invoice_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.InvoiceId, x.ChangedAt });

        b.HasOne(x => x.Invoice).WithMany(i => i.StatusHistory).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
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
