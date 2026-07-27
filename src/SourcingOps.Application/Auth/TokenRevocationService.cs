using SourcingOps.Application.Interfaces;

namespace SourcingOps.Application.Auth;

/// <summary>See <see cref="ITokenRevocationService"/> for the design rationale.</summary>
public sealed class TokenRevocationService : ITokenRevocationService
{
    private const string KeyPrefix = "auth:revoked:";

    private readonly ICacheService _cache;
    private readonly AuthOptions _options;

    public TokenRevocationService(ICacheService cache, AuthOptions options)
    {
        _cache = cache;
        _options = options;
    }

    public Task RevokeAllIssuedBeforeNowAsync(Guid userId, CancellationToken ct = default)
    {
        // TTL = the access-token lifetime (TECH_SPEC §4.2, ~8h): the exact bound that makes
        // the entry safe to drop once every token it could possibly reject has already
        // expired on its own.
        var ttl = TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes);
        return _cache.SetAsync(Key(userId), DateTime.UtcNow, ttl, ct);
    }

    public async Task<bool> IsRevokedAsync(Guid userId, DateTime tokenIssuedAtUtc, CancellationToken ct = default)
    {
        // NOTE: ICacheService.GetAsync<T>'s "T?" is a no-op for an unconstrained value-type
        // T (C# only turns "T?" into Nullable<T> for a `where T : struct` constraint) — so a
        // cache miss comes back as default(DateTime), not null. default(DateTime) is
        // DateTime.MinValue, which RevokeAllIssuedBeforeNowAsync (always DateTime.UtcNow)
        // can never legitimately produce, so it is a safe "no revocation recorded" sentinel.
        var revokedAt = await _cache.GetAsync<DateTime>(Key(userId), ct);
        if (revokedAt == default)
        {
            return false;
        }

        // Hazard 1 (self-lockout ordering) fix: a JWT `iat` (NumericDate) is ALWAYS
        // floor-truncated to whole seconds by the token library — `tokenIssuedAtUtc`
        // arriving here from a real decoded token therefore never carries a sub-second
        // component. `revokedAt`, though, is a raw `DateTime.UtcNow` WITH sub-second
        // precision. Comparing those two directly is asymmetric: a token minted a fraction
        // of a second after a revocation, but truncated back into the SAME whole second,
        // would otherwise look like it pre-dates the (more precise) revocation timestamp —
        // exactly the restore-then-immediate-login case the coordinator's brief calls out.
        // Flooring the revocation timestamp to the same whole-second resolution before
        // comparing removes that asymmetry.
        var revokedAtFloorSeconds = TruncateToSeconds(revokedAt);

        // Strictly-before only. A token issued in the same second as (or after) the
        // recorded revocation is accepted — this tolerance is what keeps a
        // revoke-then-reissue sequence self-lockout-safe; see the interface doc comment.
        return tokenIssuedAtUtc < revokedAtFloorSeconds;
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);

    private static string Key(Guid userId) => $"{KeyPrefix}{userId:N}";
}
