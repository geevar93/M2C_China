using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Dispatching;

/// <summary>
/// ACTION_PLAN E9-10 — temporary public share links for dispatched documents.
///
/// <para><b>Why this exists.</b> A `wa.me` click-to-chat URL can pre-fill text and nothing else
/// (FSD A2 fixes Phase 1 to deep links, no Business API), so until now the dispatch dialog asked
/// staff to download the PDF and attach it by hand inside WhatsApp. Putting a fetchable URL in
/// the message body removes that manual step without touching the A2 constraint: WhatsApp still
/// only ever receives text.</para>
///
/// <para><b>The security decisions, stated rather than implied</b> — this is a narrow, deliberate
/// exception to TECH_SPEC §8 / E6-04's "never a public static path", so each one is on the
/// record:</para>
/// <list type="number">
///   <item><b>The token is the capability.</b> 256 bits of CSPRNG output
///     (<see cref="IShareTokenFactory"/>), never a sequential or derivable id. Knowing a
///     document's Guid tells an attacker nothing about its share URL.</item>
///   <item><b>Stored hashed.</b> Only a SHA-256 digest is persisted, following
///     <c>RefreshToken</c>'s precedent from E1-07, so read access to the database or a backup
///     does not yield working links.</item>
///   <item><b>Scoped to one document.</b> A link resolves to exactly one target row; it is not a
///     session, carries no identity, and grants nothing else.</item>
///   <item><b>Short-lived and revocable.</b> 48 hours by default
///     (<see cref="DispatchOptions.ShareLinkLifetimeHours"/> documents the number), plus
///     <see cref="RevokeAsync"/> for killing one early.</item>
///   <item><b>Repeat use inside the window, deliberately not single-use.</b> A recipient re-opens
///     the chat, taps again after a failed download, or forwards it to a colleague in the same
///     buying team — all normal, all broken by burn-on-read. Decisively: several WhatsApp
///     clients prefetch link previews, which would consume a single-use token before the human
///     ever tapped it, making the feature fail exactly when it looked like it worked. Every
///     access is instead counted and audited.</item>
///   <item><b>Uniform 404.</b> Unknown, expired, revoked, dangling and file-missing all return
///     the same nothing. A distinct 410 for "expired" would confirm to a stranger that a guessed
///     token was once real — a free oracle for anyone probing, and worth more to an attacker
///     than the politeness is worth to a legitimate recipient, who gets a clear message from the
///     staff member instead.</item>
/// </list>
///
/// <para><b>Mint per compose, never "mint or reuse".</b> Reusing a still-live link for the same
/// document would mean returning its URL a second time — impossible once the token is stored
/// hashed, since the raw value is unrecoverable by design. The only way to "reuse" a row is to
/// rotate its token, and that silently kills a link the customer may already have been sent,
/// which is strictly worse than an extra 150-byte row. So each compose mints its own link; an
/// abandoned one simply expires unused, and the extra rows double as a record of when a document
/// was prepared for sharing and by whom.</para>
///
/// <para><b>Cleanup.</b> Expired rows are deliberately kept. They are the record of what was
/// shared publicly and when, which is precisely what the FSD's Auditability NFR would want
/// preserved; deleting them would also need the background worker TECH_SPEC §4.8 explicitly
/// rules out for Phase 1. At realistic dispatch volumes the table grows by a few thousand
/// ~150-byte rows a year. Revisit only if that stops being true.</para>
/// </summary>
public sealed class DocumentShareLinkService : IDocumentShareLinkService
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IShareTokenFactory _tokens;
    private readonly IAuditLogger _audit;
    private readonly DispatchOptions _options;

    public DocumentShareLinkService(
        IAppDbContext db, IFileStorage fileStorage, IShareTokenFactory tokens, IAuditLogger audit, DispatchOptions options)
    {
        _db = db;
        _fileStorage = fileStorage;
        _tokens = tokens;
        _audit = audit;
        _options = options;
    }

    // ---- Mint ---------------------------------------------------------------------------

    public async Task<DocumentShareLinkDto> MintAsync(
        string targetType, Guid targetId, Guid actorUserId, CancellationToken ct = default)
    {
        if (!DocumentShareTargetTypes.All.Contains(targetType))
        {
            throw new AppValidationException("targetType", $"'{targetType}' is not a shareable document type.");
        }

        // Resolving up front means an unknown target or an unissued invoice fails as a 400 at
        // compose time — where the staff member can act on it — rather than as a dead link the
        // customer discovers.
        _ = await ResolveTargetAsync(targetType, targetId, ct)
            ?? throw new AppValidationException(
                "targetId",
                targetType == DocumentShareTargetTypes.Invoice
                    ? "This invoice has no PDF yet — issue it before sharing a link."
                    : "The document to share was not found.");

        var now = DateTime.UtcNow;
        var token = _tokens.CreateToken();
        var link = new DocumentShareLink
        {
            Id = Guid.NewGuid(),
            TokenHash = _tokens.Hash(token),
            TargetType = targetType,
            TargetId = targetId,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            ExpiresAt = now.AddHours(_options.ShareLinkLifetimeHours),
            AccessCount = 0
        };
        _db.DocumentShareLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "ShareLinkCreated", "DocumentShareLink", link.Id.ToString(),
            new { targetType, targetId, link.ExpiresAt }, ct);

        return new DocumentShareLinkDto(link.Id, _options.BuildShareUrl(token), AsUtc(link.ExpiresAt));
    }

    // ---- Anonymous resolve --------------------------------------------------------------

    public async Task<SharedDocumentDownload?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = _tokens.Hash(token);
        var link = await _db.DocumentShareLinks.FirstOrDefaultAsync(l => l.TokenHash == hash, ct);

        var now = DateTime.UtcNow;
        if (link is null || link.RevokedAt is not null || AsUtc(link.ExpiresAt) <= now)
        {
            // One shape for every failure — see this class's doc comment on the 404/410 choice.
            return null;
        }

        var target = await ResolveTargetAsync(link.TargetType, link.TargetId, ct);
        if (target is null)
        {
            return null;
        }

        Stream stream;
        try
        {
            stream = await _fileStorage.OpenReadAsync(target.FilePath, ct);
        }
        catch (FileNotFoundException)
        {
            // A row pointing at a file that is gone is a data problem, not something to leak to
            // an anonymous caller as a 500 stack trace.
            return null;
        }

        link.AccessCount += 1;
        link.LastAccessedAt = now;
        await _db.SaveChangesAsync(ct);

        // Every anonymous access gets its own audit row, not just the counter. The counter
        // answers "how many times"; the FSD's Auditability NFR wants "when", and an
        // unauthenticated read of a business document is exactly the event an audit would be
        // opened to reconstruct. `userId` is null — IAuditLogger already models the
        // actor-unknown case (failed logins use it), and inventing a staff actor here would be
        // a lie, since the staff member shared the link, they did not open it.
        await _audit.LogAsync(null, "ShareLinkAccessed", "DocumentShareLink", link.Id.ToString(),
            new { link.TargetType, link.TargetId, link.AccessCount }, ct);

        return new SharedDocumentDownload(stream, target.FileName, target.ContentType);
    }

    // ---- Revoke -------------------------------------------------------------------------

    public async Task<ShareLinkRevokeResult> RevokeAsync(Guid shareLinkId, Guid actorUserId, CancellationToken ct = default)
    {
        var link = await _db.DocumentShareLinks.FirstOrDefaultAsync(l => l.Id == shareLinkId, ct);
        if (link is null)
        {
            return ShareLinkRevokeResult.NotFound;
        }

        var now = DateTime.UtcNow;
        if (link.RevokedAt is not null || AsUtc(link.ExpiresAt) <= now)
        {
            return ShareLinkRevokeResult.AlreadyInactive;
        }

        link.RevokedAt = now;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "ShareLinkRevoked", "DocumentShareLink", link.Id.ToString(),
            new { link.TargetType, link.TargetId, link.AccessCount }, ct);

        return ShareLinkRevokeResult.Revoked;
    }

    // ---- Target resolution ---------------------------------------------------------------

    /// <summary>
    /// The single place a (type, id) pair becomes a file. Adding a shareable document type is a
    /// case here plus a constant — see <see cref="DocumentShareTargetTypes"/> on why that is not
    /// a migration.
    /// </summary>
    private async Task<ShareTarget?> ResolveTargetAsync(string targetType, Guid targetId, CancellationToken ct)
    {
        switch (targetType)
        {
            case DocumentShareTargetTypes.CatalogDocument:
            {
                var document = await _db.CatalogDocuments.FirstOrDefaultAsync(d => d.Id == targetId, ct);
                return document is null
                    ? null
                    : new ShareTarget(document.FilePath, document.OriginalFilename, "application/pdf");
            }

            case DocumentShareTargetTypes.Invoice:
            {
                var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == targetId, ct);
                // A Draft invoice has no rendered PDF (E8-03 renders on issue) — indistinguishable
                // from "no such invoice" as far as sharing is concerned.
                return invoice?.PdfFilePath is null
                    ? null
                    : new ShareTarget(invoice.PdfFilePath, $"{invoice.InvoiceNumber}.pdf", "application/pdf");
            }

            default:
                return null;
        }
    }

    private sealed record ShareTarget(string FilePath, string FileName, string ContentType);

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
