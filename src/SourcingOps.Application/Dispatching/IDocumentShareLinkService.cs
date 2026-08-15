namespace SourcingOps.Application.Dispatching;

/// <summary>
/// ACTION_PLAN E9-10. Mints, resolves and revokes the temporary public links that let a
/// click-to-chat message carry the document itself instead of instructions to attach it.
/// </summary>
public interface IDocumentShareLinkService
{
    /// <summary>
    /// Mints a fresh live link for the given target. Requires the caller to already hold
    /// <c>Dispatch.Send</c> — this method does not check permissions itself. Throws
    /// <c>Common.AppValidationException</c> when the target does not exist or has no stored file
    /// yet (e.g. a Draft invoice, whose PDF is only rendered on issue). See the implementation's
    /// doc comment for why this deliberately does not reuse an existing live link.
    /// </summary>
    Task<DocumentShareLinkDto> MintAsync(string targetType, Guid targetId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// The anonymous read path. Returns null — never throws, never distinguishes — for an
    /// unknown, expired, revoked, or dangling token, and for a token whose underlying file has
    /// gone missing from storage. The caller turns every null into the same 404.
    /// </summary>
    Task<SharedDocumentDownload?> ResolveAsync(string token, CancellationToken ct = default);

    /// <summary>E9-10: kill a link before its expiry. Idempotent — see <see cref="ShareLinkRevokeResult"/>.</summary>
    Task<ShareLinkRevokeResult> RevokeAsync(Guid shareLinkId, Guid actorUserId, CancellationToken ct = default);
}
