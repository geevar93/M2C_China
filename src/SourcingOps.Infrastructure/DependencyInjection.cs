using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Audit;
using SourcingOps.Infrastructure.Auth;
using SourcingOps.Infrastructure.Caching;
using SourcingOps.Infrastructure.Persistence;
using SourcingOps.Infrastructure.Persistence.Seed;
using SourcingOps.Infrastructure.Storage;

namespace SourcingOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddAppCaching(configuration);

        var jwtOptions = new JwtOptions();
        configuration.GetSection("Jwt").Bind(jwtOptions);
        services.AddSingleton(jwtOptions);

        var storageOptions = new FileStorageOptions();
        configuration.GetSection("Storage").Bind(storageOptions);
        services.AddSingleton(storageOptions);

        var bootstrapOptions = new BootstrapAdminOptions();
        configuration.GetSection("Bootstrap").Bind(bootstrapOptions);
        services.AddSingleton(bootstrapOptions);

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenGenerator>(sp =>
            new JwtTokenGenerator(sp.GetRequiredService<JwtOptions>(), sp.GetRequiredService<AuthOptions>()));
        services.AddScoped<IAuditLogger, EfAuditLogger>();
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();

        services.AddScoped<DbSeeder>();

        return services;
    }
}
