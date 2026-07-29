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

    public DispatchController(IDispatchService service)
    {
        _service = service;
    }

    [HttpGet("compose")]
    [Authorize(Policy = PermissionCodes.DispatchSend)]
    public async Task<IActionResult> Compose([FromQuery] Guid customerId, [FromQuery] Guid catalogDocumentId, CancellationToken ct)
    {
        var result = await _service.ComposeAsync(customerId, catalogDocumentId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.DispatchSend)]
    public async Task<IActionResult> Create([FromBody] CreateDispatchLogRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
