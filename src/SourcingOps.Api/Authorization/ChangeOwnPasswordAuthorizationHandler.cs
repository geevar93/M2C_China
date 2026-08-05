using Microsoft.AspNetCore.Authorization;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Authorization;

/// <summary>
/// Evaluates <see cref="ChangeOwnPasswordRequirement"/> against the caller's JWT. Succeeds if
/// EITHER branch holds:
///
/// <list type="number">
/// <item><b>The forced remediation path</b> — the token carries
/// <c>must_change_password=true</c> (<see cref="Infrastructure.Auth.JwtTokenGenerator"/> writes it
/// as the lowercase string "true"/"false"). Open to every user regardless of permissions. This
/// branch is not optional: E11-01 creates every account with <c>MustChangePassword=true</c>, and
/// while that flag is set <c>RequirePasswordChangeFilter</c> 403s every endpoint except this one.
/// Gate this endpoint on the permission alone and such a user can never activate their account —
/// the same class of unrecoverable lockout the "last active Super Admin" guard exists to prevent
/// (TECH_SPEC §4.4).</item>
///
/// <item><b>The voluntary path</b> — the caller holds
/// <see cref="PermissionCodes.AccountChangeOwnPassword"/>. That code is in
/// <see cref="PermissionCodes.AdminOnly"/>, so the seeder grants it to SuperAdmin only. Permission
/// claims arrive as repeated "permissions" claims — see
/// <see cref="PermissionAuthorizationHandler"/>'s doc comment for how the JWT shapes them.</item>
/// </list>
/// </summary>
public sealed class ChangeOwnPasswordAuthorizationHandler : AuthorizationHandler<ChangeOwnPasswordRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ChangeOwnPasswordRequirement requirement)
    {
        var mustChangePassword = context.User.FindFirst("must_change_password")?.Value;
        if (string.Equals(mustChangePassword, "true", StringComparison.OrdinalIgnoreCase)
            || context.User.HasClaim("permissions", PermissionCodes.AccountChangeOwnPassword))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
