namespace SourcingOps.Application.Dispatching;

/// <summary>
/// E9-10: the mintable half of a compose response. <see cref="Url"/> carries the raw token and
/// is the only place it is ever returned — the database holds a hash, so this value cannot be
/// recovered afterwards. <see cref="Id"/> is the handle for revoking the link early.
/// </summary>
public sealed record DocumentShareLinkDto(Guid Id, string Url, DateTime ExpiresAtUtc);

/// <summary>
/// E9-10: what the anonymous endpoint needs to stream the file. Shaped like
/// <c>CatalogDocumentDownload</c> and friends deliberately — the public path serves the same
/// bytes from the same <c>IFileStorage</c>, it just reaches them through a token instead of a
/// permission check.
/// </summary>
public sealed record SharedDocumentDownload(Stream Content, string FileName, string ContentType);

/// <summary>E9-10: outcome of an early revoke, so the controller can distinguish 404 from a no-op.</summary>
public enum ShareLinkRevokeResult
{
    NotFound,
    Revoked,

    /// <summary>Already revoked, or already expired. Treated as success — revoking is idempotent.</summary>
    AlreadyInactive
}
