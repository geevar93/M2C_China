using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Caching;

/// <summary>
/// Single switch point for TECH_SPEC §4.5 / constraint C2: reads `Caching:Provider` and
/// registers exactly one <see cref="ICacheService"/> implementation. No other code may
/// reference `IMemoryCache`/`IDistributedCache` directly — this is the only place that does.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddAppCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Caching:Provider"] ?? "InMemory";

        if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration["Caching:Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Caching:Provider is set to 'Redis' but Caching:Redis:ConnectionString is empty.");
            }

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = configuration["Caching:Redis:InstanceName"] ?? "sourcingops:";
            });
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            var sizeLimitMb = configuration.GetValue<int?>("Caching:InMemory:SizeLimitMb") ?? 64;
            services.AddMemoryCache(options =>
            {
                // See MemoryCacheService for why this is an approximate entry-count
                // budget, not a byte-accurate cap.
                options.SizeLimit = sizeLimitMb * 256;
            });
            services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        return services;
    }
}
