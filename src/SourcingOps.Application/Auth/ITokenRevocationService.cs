namespace SourcingOps.Application.Auth;

/// <summary>
/// Deny-list for already-issued JWT access tokens (ACTION_PLAN N-7). A JWT can't be "found"
/// and individually revoked once issued — there is nothing to look up by token id without
/// tracking every access token ever minted. Instead this keys by USER id and stores a single
/// revocation timestamp: "everything issued to this user before time T is no longer valid".
/// The auth pipeline then rejects any token whose own `iat` predates that timestamp. TTL on
/// the stored value equals the access-token lifetime — after that, every pre-revocation token
/// has expired via ordinary JWT `exp` validation anyway, so the entry is dead weight and can
/// be dropped. That is what bounds the deny-list's size automatically, with no cleanup job.
///
/// CAVEAT — read before changing the deployment topology: the default <c>ICacheService</c>
/// implementation (<c>MemoryCacheService</c>) is in-memory and therefore per-process
/// (TECH_SPEC §4.5, C2). With today's single-API-instance topology (TECH_SPEC §7.2, OI-6)
/// that is sound: there is only one process to be revoked in. If the deployment ever runs
/// two or more API instances without switching <c>Caching:Provider=Redis</c>, revocation
/// becomes per-instance and INCOMPLETE — a user revoked on instance A could still be served
/// by instance B until that token's natural `exp`. This is a real constraint to document at
/// deploy time, not a reason to avoid the deny-list approach.
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>
    /// Marks every access token issued to <paramref name="userId"/> up to and including this
    /// moment as revoked. Uses an equal-or-after tolerance on the token's own `iat` (see
    /// <see cref="IsRevokedAsync"/>) specifically so a revoke-then-immediately-reissue
    /// sequence (e.g. a future password-reset/change-password flow) can never invalidate the
    /// very token it just handed the caller, even if both timestamps land in the same
    /// JWT-resolution second.
    /// </summary>
    Task RevokeAllIssuedBeforeNowAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// True if <paramref name="tokenIssuedAtUtc"/> — the token's own `iat`, decoded from the
    /// validated JWT, never trusted from anywhere else — is STRICTLY BEFORE the last recorded
    /// revocation for <paramref name="userId"/>. A token issued at exactly the recorded
    /// revocation instant, or after it, is treated as valid (not revoked).
    ///
    /// Implementation note (hazard 1, sub-second `iat` truncation): a JWT `iat` is always a
    /// whole-second Unix timestamp, but the stored revocation instant is a full-precision
    /// <see cref="DateTime"/>. The implementation floors the stored value to whole-second
    /// resolution before comparing, so a token minted a fraction of a second after a
    /// revocation but landing in the same integer second — e.g. restore, then an immediate
    /// re-login — is never mistaken for one that pre-dates it.
    /// </summary>
    Task<bool> IsRevokedAsync(Guid userId, DateTime tokenIssuedAtUtc, CancellationToken ct = default);
}
