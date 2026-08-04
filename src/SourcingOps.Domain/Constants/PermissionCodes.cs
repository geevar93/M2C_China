namespace SourcingOps.Domain.Constants;

/// <summary>
/// The static permission catalog. Every value here is a seeded row in the
/// <c>permissions</c> table (see TECH_SPEC §4.3). Adding a new permission is a
/// one-line addition to this list plus a seed entry — no new plumbing.
/// </summary>
public static class PermissionCodes
{
    public const string CustomersView = "Customers.View";
    public const string CustomersEdit = "Customers.Edit";

    public const string VendorsView = "Vendors.View";
    public const string VendorsEdit = "Vendors.Edit";

    public const string CatalogsView = "Catalogs.View";
    public const string CatalogsEdit = "Catalogs.Edit";

    public const string InventoryView = "Inventory.View";
    public const string InventoryEdit = "Inventory.Edit";

    /// <summary>
    /// N-38. Gates only <c>POST /inventory/{id}/adjustments</c> — deliberately NOT added to
    /// <see cref="AdminOnly"/>. The staff who physically count stock are exactly who needs to
    /// record a correction, so Super-Admin-gating this would block the workflow it exists to
    /// serve; the seeded Associate role gets it like every other non-Admin.* permission.
    /// </summary>
    public const string InventoryAdjust = "Inventory.Adjust";

    public const string ShipmentsView = "Shipments.View";
    public const string ShipmentsEdit = "Shipments.Edit";

    public const string InvoicingView = "Invoicing.View";
    public const string InvoicingEdit = "Invoicing.Edit";
    public const string InvoicingMarkPaid = "Invoicing.MarkPaid";

    public const string DispatchSend = "Dispatch.Send";

    public const string AnalyticsView = "Analytics.View";

    public const string AdminManageUsers = "Admin.ManageUsers";
    public const string AdminManageMasterData = "Admin.ManageMasterData";

    /// <summary>
    /// The full catalog, in seed order. Startup seeding and policy registration
    /// both iterate this single list so a new permission never needs new plumbing.
    /// </summary>
    public static readonly string[] All =
    [
        CustomersView, CustomersEdit,
        VendorsView, VendorsEdit,
        CatalogsView, CatalogsEdit,
        InventoryView, InventoryEdit, InventoryAdjust,
        ShipmentsView, ShipmentsEdit,
        InvoicingView, InvoicingEdit, InvoicingMarkPaid,
        DispatchSend,
        AnalyticsView,
        AdminManageUsers, AdminManageMasterData
    ];

    /// <summary>Permissions withheld from the seeded Associate role.</summary>
    public static readonly string[] AdminOnly = [AdminManageUsers, AdminManageMasterData];
}
