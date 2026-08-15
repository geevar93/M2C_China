using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Dispatching;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E9: TECH_SPEC §4.7's binding route is <c>POST /dispatch-log</c>; <c>GET
/// /dispatch-log/compose</c> is an additive read (E9-01/E9-06) that lets a caller fetch the
/// rendered template + `wa.me` link before recording the send — both gated on
/// <see cref="PermissionCodes.DispatchSend"/> per FSD Q4/OI-8 (any staff may dispatch). The
/// staff user always comes from the token (<see cref="ClaimsPrincipalExtensions.GetRequiredUserId"/>),
/// never the request body — see <see cref="CreateDispatchLogRequest"/>'s doc comment.
/// </summary>
[ApiController]
[Route("api/v1/dispatch-log")]
[Authorize]
public sealed class DispatchController : ControllerBase
{
    private readonly IDispatchService _service;
    private readonly IDocumentShareLinkService _shareLinks;

    public DispatchController(IDispatchService service, IDocumentShareLinkService shareLinks)
    {
        _service = service;
        _shareLinks = shareLinks;
    }

    /// <summary>
    /// E9-10: also mints the temporary public share link embedded in the returned message, which
    /// is why this read endpoint writes a row and why it is gated on
    /// <see cref="PermissionCodes.DispatchSend"/> rather than a view permission — issuing an
    /// unauthenticated URL to a business document is a send-class action, not a read.
    /// <paramref name="invoiceId"/> is the E9-10 widening; exactly one of the two is required.
    /// </summary>
    [HttpGet("compose")]
    [Authorize(Policy = PermissionCodes.DispatchSend)]
    public async Task<IActionResult> Compose(
        [FromQuery] Guid customerId,
        [FromQuery] Guid? catalogDocumentId,
        [FromQuery] Guid? invoiceId,
        CancellationToken ct)
    {
        var result = await _service.ComposeAsync(customerId, catalogDocumentId, invoiceId, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// E9-10: kill a share link before it expires — the "I sent that to the wrong customer"
    /// escape hatch. Gated on the same permission as minting: whoever can hand out a public link
    /// can take it back. Idempotent, so a double-tap or a link that expired in the meantime is a
    /// 204 rather than an error the caller has to interpret.
    /// </summary>
    [HttpPost("share-links/{id:guid}/revoke")]
    [Authorize(Policy = PermissionCodes.DispatchSend)]
    public async Task<IActionResult> RevokeShareLink(Guid id, CancellationToken ct)
    {
        var result = await _shareLinks.RevokeAsync(id, User.GetRequiredUserId(), ct);
        return result == ShareLinkRevokeResult.NotFound ? NotFound() : NoContent();
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.DispatchSend)]
    public async Task<IActionResult> Create([FromBody] CreateDispatchLogRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
