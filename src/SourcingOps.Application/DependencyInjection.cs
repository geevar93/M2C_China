using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Dispatching;
using SourcingOps.Application.Inventory;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Shipments;
using SourcingOps.Application.Vendors;

namespace SourcingOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var authOptions = new AuthOptions();
        configuration.GetSection("Auth").Bind(authOptions);
        services.AddSingleton(authOptions);

        var customerOptions = new CustomerOptions();
        configuration.GetSection("Customers").Bind(customerOptions);
        services.AddSingleton(customerOptions);

        // Bound from the same "Storage" config section Infrastructure's FileStorageOptions
        // reads (Storage:MaxUploadSizeBytes) — see CatalogUploadOptions's doc comment for why
        // Application keeps its own POCO instead of referencing Infrastructure's type.
        var catalogUploadOptions = new CatalogUploadOptions();
        configuration.GetSection("Storage").Bind(catalogUploadOptions);
        services.AddSingleton(catalogUploadOptions);

        var dispatchOptions = new DispatchOptions();
        configuration.GetSection("Dispatch").Bind(dispatchOptions);
        services.AddSingleton(dispatchOptions);

        // Bound from the same "Storage" config section as CatalogUploadOptions — see
        // VendorUploadOptions's doc comment for why Application keeps its own POCO per
        // feature rather than sharing CatalogUploadOptions across tracks.
        var vendorUploadOptions = new VendorUploadOptions();
        configuration.GetSection("Storage").Bind(vendorUploadOptions);
        services.AddSingleton(vendorUploadOptions);

        // E7-09 — same "Storage" section again, same per-feature-POCO reasoning.
        var shipmentUploadOptions = new ShipmentUploadOptions();
        configuration.GetSection("Storage").Bind(shipmentUploadOptions);
        services.AddSingleton(shipmentUploadOptions);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<ITokenRevocationService, TokenRevocationService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IVendorDocumentService, VendorDocumentService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IDispatchService, DispatchService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IShipmentDocumentService, ShipmentDocumentService>();

        return services;
    }
}
