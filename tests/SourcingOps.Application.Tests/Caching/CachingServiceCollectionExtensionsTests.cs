using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Caching;

namespace SourcingOps.Application.Tests.Caching;

/// <summary>
/// TECH_SPEC §4.5, C2: the `Caching:Provider` switch must be config-only. Covers both
/// branches of AddAppCaching's single decision point.
/// </summary>
public class CachingServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddAppCaching_DefaultProvider_RegistersMemoryCacheService()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddAppCaching(config);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICacheService>().Should().BeOfType<MemoryCacheService>();
    }

    [Fact]
    public void AddAppCaching_ProviderInMemory_RegistersMemoryCacheService()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?> { ["Caching:Provider"] = "InMemory" });

        services.AddAppCaching(config);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICacheService>().Should().BeOfType<MemoryCacheService>();
    }

    [Fact]
    public void AddAppCaching_ProviderRedis_RegistersRedisCacheService()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Caching:Provider"] = "Redis",
            ["Caching:Redis:ConnectionString"] = "localhost:6379"
        });

        services.AddAppCaching(config);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICacheService>().Should().BeOfType<RedisCacheService>();
    }

    [Fact]
    public void AddAppCaching_ProviderRedisWithoutConnectionString_ThrowsAtStartup()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?> { ["Caching:Provider"] = "Redis" });

        var act = () => services.AddAppCaching(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task MemoryCacheService_SetThenGet_RoundTripsValue()
    {
        var services = new ServiceCollection();
        services.AddAppCaching(BuildConfig(new Dictionary<string, string?>()));
        var cache = services.BuildServiceProvider().GetRequiredService<ICacheService>();

        await cache.SetAsync("key1", "hello", TimeSpan.FromMinutes(1));
        var result = await cache.GetAsync<string>("key1");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task MemoryCacheService_GetOrCreateAsync_OnlyInvokesFactoryOnce()
    {
        var services = new ServiceCollection();
        services.AddAppCaching(BuildConfig(new Dictionary<string, string?>()));
        var cache = services.BuildServiceProvider().GetRequiredService<ICacheService>();
        var callCount = 0;

        async Task<int> Factory(CancellationToken ct)
        {
            callCount++;
            await Task.Yield();
            return 42;
        }

        var first = await cache.GetOrCreateAsync("key2", TimeSpan.FromMinutes(1), Factory);
        var second = await cache.GetOrCreateAsync("key2", TimeSpan.FromMinutes(1), Factory);

        first.Should().Be(42);
        second.Should().Be(42);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task MemoryCacheService_RemoveAsync_ClearsTheValue()
    {
        var services = new ServiceCollection();
        services.AddAppCaching(BuildConfig(new Dictionary<string, string?>()));
        var cache = services.BuildServiceProvider().GetRequiredService<ICacheService>();
        await cache.SetAsync("key3", "value", TimeSpan.FromMinutes(1));

        await cache.RemoveAsync("key3");
        var result = await cache.GetAsync<string>("key3");

        result.Should().BeNull();
    }
}
