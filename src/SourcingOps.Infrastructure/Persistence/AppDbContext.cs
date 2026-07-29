using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<LeadStatus> LeadStatuses => Set<LeadStatus>();
    public DbSet<ShipmentStatus> ShipmentStatuses => Set<ShipmentStatus>();
    public DbSet<InvoiceStatus> InvoiceStatuses => Set<InvoiceStatus>();
    public DbSet<VendorStatus> VendorStatuses => Set<VendorStatus>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerCategory> CustomerCategories => Set<CustomerCategory>();
    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorCategory> VendorCategories => Set<VendorCategory>();
    public DbSet<VendorDocument> VendorDocuments => Set<VendorDocument>();

    public DbSet<CatalogSection> CatalogSections => Set<CatalogSection>();
    public DbSet<CatalogDocument> CatalogDocuments => Set<CatalogDocument>();

    public DbSet<Dispatch> Dispatches => Set<Dispatch>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<ShipmentDocument> ShipmentDocuments => Set<ShipmentDocument>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
