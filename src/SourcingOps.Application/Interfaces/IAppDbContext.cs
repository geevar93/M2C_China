using Microsoft.EntityFrameworkCore;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Interfaces;

/// <summary>
/// Narrow persistence seam. `Application` depends only on this interface — never on
/// `DbContext` directly and never through a generic repository (TECH_SPEC §4.1). The
/// concrete `AppDbContext` in `Infrastructure` implements it.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Category> Categories { get; }
    DbSet<ServiceType> ServiceTypes { get; }
    DbSet<LeadStatus> LeadStatuses { get; }
    DbSet<ShipmentStatus> ShipmentStatuses { get; }
    DbSet<InvoiceStatus> InvoiceStatuses { get; }
    DbSet<VendorStatus> VendorStatuses { get; }
    DbSet<DocumentType> DocumentTypes { get; }

    DbSet<Customer> Customers { get; }
    DbSet<CustomerCategory> CustomerCategories { get; }
    DbSet<Interaction> Interactions { get; }

    DbSet<Vendor> Vendors { get; }
    DbSet<VendorCategory> VendorCategories { get; }
    DbSet<VendorDocument> VendorDocuments { get; }

    DbSet<CatalogSection> CatalogSections { get; }
    DbSet<CatalogDocument> CatalogDocuments { get; }

    DbSet<Dispatch> Dispatches { get; }

    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<InventoryInboundEntry> InventoryInboundEntries { get; }

    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentLine> ShipmentLines { get; }
    DbSet<ShipmentStatusHistory> ShipmentStatusHistory { get; }
    DbSet<ShipmentDocument> ShipmentDocuments { get; }

    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceStatusHistory> InvoiceStatusHistory { get; }

    DbSet<CompanySettings> CompanySettings { get; }

    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
