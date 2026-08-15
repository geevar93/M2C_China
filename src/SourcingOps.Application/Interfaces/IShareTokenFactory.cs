namespace SourcingOps.Application.Interfaces;

/// <summary>
/// E9-10: mints and hashes the opaque token behind a public document share link. Same
/// Application-declares/Infrastructure-implements split as <see cref="IPasswordHasher"/> — the
/// cryptographic primitive choice is an Infrastructure concern, and keeping it behind an
/// interface is what lets <c>DocumentShareLinkService</c>'s unit tests assert behaviour
/// (reuse, expiry, revocation) without depending on real randomness.
/// </summary>
public interface IShareTokenFactory
{
    /// <summary>
    /// A fresh, unguessable, URL-safe token. Implementations must use a cryptographic RNG —
    /// the token IS the access capability, so a predictable one would hand out every document.
    /// </summary>
    string CreateToken();

    /// <summary>
    /// The stable digest stored in <c>document_share_links.token_hash</c> and used for lookup.
    /// Must be deterministic (unsalted) — the lookup is "find the row for this token", which a
    /// per-row salt would make impossible without scanning every row.
    /// </summary>
    string Hash(string token);
}
