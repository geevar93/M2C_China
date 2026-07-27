using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Admin;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// Binding admin-users contract fixed by the coordinator (TECH_SPEC §4.4; ACTION_PLAN
/// E11-01…E11-05). Every route requires <see cref="PermissionCodes.AdminManageUsers"/>.
/// Also exposes <c>POST /users/{id}/restore</c> — an addition beyond the written stories,
/// flagged in the build report — since E11-03's deactivate is otherwise the only operation
/// on an account with no way back through the API.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = PermissionCodes.AdminManageUsers)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _service;

    public AdminUsersController(IAdminUserService service)
    {
        _service = service;
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(search, page, pageSize, includeInactive, ct);
        return Ok(result);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        var result = await _service.ResetPasswordAsync(id, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var found = await _service.DeactivateAsync(id, User.GetRequiredUserId(), ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPost("users/{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var result = await _service.RestoreAsync(id, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("users/{id:guid}/roles")]
    public async Task<IActionResult> AssignRoles(Guid id, [FromBody] AssignRolesRequest request, CancellationToken ct)
    {
        var result = await _service.AssignRolesAsync(id, request.RoleIds, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
    {
        var roles = await _service.ListRolesAsync(ct);
        return Ok(roles);
    }
}
