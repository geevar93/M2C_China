using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Dispatching;
using SourcingOps.Application.MasterData;
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

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<ITokenRevocationService, TokenRevocationService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IDispatchService, DispatchService>();

        return services;
    }
}
