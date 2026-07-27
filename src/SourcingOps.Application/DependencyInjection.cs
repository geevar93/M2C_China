using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Auth;

namespace SourcingOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var authOptions = new AuthOptions();
        configuration.GetSection("Auth").Bind(authOptions);
        services.AddSingleton(authOptions);

        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
