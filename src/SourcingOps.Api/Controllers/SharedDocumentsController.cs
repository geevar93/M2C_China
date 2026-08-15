using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingOps.Application.Dispatching;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E9-10 — the one intentionally anonymous document endpoint in the system.
///
/// <para>Everything else that serves a stored file (<see cref="CatalogDocumentsController"/>,
/// <see cref="VendorDocumentsController"/>, <see cref="ShipmentDocumentsController"/>,
/// <see cref="InvoicesController"/>'s PDF route) is <c>[Authorize(Policy = …)]</c>, per
/// TECH_SPEC §8 and E6-04. This controller is the deliberate exception that makes WhatsApp
/// dispatch actually deliver a document: the recipient is a customer with no account and never
/// will have one in Phase 1 (FSD A1 — internal-only tool, no customer logins), so there is no
/// credential to check. The token in the path IS the authorisation, and
/// <see cref="DocumentShareLinkService"/>'s doc comment records exactly what bounds it.</para>
///
/// <para><b>Anonymous means anonymous.</b> A missing, wrong, expired or revoked token must never
/// produce a 401 or a WWW-Authenticate challenge — that would tell the recipient "log in", which
/// they cannot do, and would tell a prober that authentication is the thing standing between
/// them and the file. Every failure is the same 404 with no body detail.</para>
///
/// <para>Rate-limited per IP as defence in depth. A 256-bit token is not brute-forceable and the
/// limiter is not what makes this safe — it just means a scanner burns its own bandwidth instead
/// of the box's, using the same built-in middleware TECH_SPEC §4.8 already mandates for login
/// (no new dependency, per C1).</para>
/// </summary>
[ApiController]
[Route("api/v1/shared-documents")]
[AllowAnonymous]
public sealed class SharedDocumentsController : ControllerBase
{
    private readonly IDocumentShareLinkService _shareLinks;

    public SharedDocumentsController(IDocumentShareLinkService shareLinks)
    {
        _shareLinks = shareLinks;
    }

    /// <summary>
    /// Serves the shared file while its token is live. Served <c>inline</c> rather than as an
    /// attachment: the recipient is on a phone opening a link from a chat, and a viewer opening
    /// in place is the behaviour that makes this feature worth building — a forced download to a
    /// phone's file system is barely better than the manual attach step it replaces.
    /// </summary>
    [HttpGet("{token}")]
    [EnableRateLimiting(RateLimiterPolicies.SharedDocument)]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var download = await _shareLinks.ResolveAsync(token, ct);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(download.FileName)}\"";

        // Cache-Control matters here in a way it does not on the authenticated routes: this URL
        // is fetched by phone browsers and, on some clients, by WhatsApp's own link-preview
        // crawler. Telling shared caches not to keep the bytes limits how far a document can
        // travel beyond the expiry window this feature's whole safety argument rests on.
        Response.Headers.CacheControl = "private, no-store";

        return File(download.Content, download.ContentType);
    }
}
