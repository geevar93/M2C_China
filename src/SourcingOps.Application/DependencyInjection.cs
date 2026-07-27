using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Crm;
using SourcingOps.Application.MasterData;

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

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<ITokenRevocationService, TokenRevocationService>();
        services.AddScoped<ICustomerService, CustomerService>();

        return services;
    }
}
