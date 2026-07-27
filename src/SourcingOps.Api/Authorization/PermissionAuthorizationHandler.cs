using Microsoft.AspNetCore.Authorization;

namespace SourcingOps.Api.Authorization;

/// <summary>
/// Evaluates a <see cref="PermissionRequirement"/> against the caller's JWT. The token
/// carries the resolved permission set at login time (TECH_SPEC §4.3) as repeated
/// "permissions" claims — <see cref="Infrastructure.Auth.JwtTokenGenerator"/> writes them as
/// an explicit JSON array in the payload so JwtBearer deserializes one <c>Claim</c> per
/// array element (including zero or one), never a single collapsed scalar claim.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim("permissions", requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
