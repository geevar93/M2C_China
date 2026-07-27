using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingOps.Api.Filters;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Controllers;

/// <summary>Binding auth contract per the coordinator's fixed spec — see task brief. FR-ADM-03.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        if (result is null)
        {
            // Deliberately generic — no hint of whether the email exists (TECH_SPEC §4.2).
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
        }

        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(request, ct);
        if (result is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid or expired refresh token.");
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _authService.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    [AllowMustChangePassword]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid token subject.");
        }

        var result = await _authService.ChangePasswordAsync(userId, request, ct);

        return result.Outcome switch
        {
            ChangePasswordOutcome.Success => Ok(result.Result),
            ChangePasswordOutcome.IncorrectCurrentPassword => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Current password is incorrect."),
            _ => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Account not found.")
        };
    }
}
