using Microsoft.Extensions.Caching.Memory;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Caching;

/// <summary>Default cache implementation (TECH_SPEC §4.5, C2) — wraps the built-in <see cref="IMemoryCache"/>.</summary>
public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        _cache.Set(key, value, BuildOptions(ttl));
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(ct);
        _cache.Set(key, value, BuildOptions(ttl));
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    // IMemoryCache has no built-in way to measure an arbitrary object's actual byte size,
    // so when Caching:InMemory:SizeLimitMb is configured we treat it as an approximate
    // entry-count budget (assuming ~4KB/entry) rather than an exact memory cap — see
    // CachingServiceCollectionExtensions for where SizeLimit itself is set.
    private static MemoryCacheEntryOptions BuildOptions(TimeSpan ttl) =>
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl).SetSize(1);
}
