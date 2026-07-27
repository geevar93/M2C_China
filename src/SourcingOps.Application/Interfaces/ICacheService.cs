namespace SourcingOps.Application.Interfaces;

/// <summary>
/// Cache abstraction (TECH_SPEC §4.5, C2). Application/Infrastructure code depends
/// only on this — never on `IMemoryCache`/`IDistributedCache` directly — so switching
/// `Caching:Provider` is a config change, not a code change.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
