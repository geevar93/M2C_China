using Microsoft.AspNetCore.Authorization;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Authorization;

/// <summary>
/// Backs the <c>"Auth.ChangePassword"</c> policy on <c>POST /api/v1/auth/change-password</c>.
/// Unlike <see cref="PermissionRequirement"/> this is not a plain "hold this code" check — it is
/// satisfied by EITHER of two disjoint callers, which is a correctness requirement rather than a
/// convenience. See <see cref="ChangeOwnPasswordAuthorizationHandler"/> for both branches and why
/// dropping either one breaks a real flow.
/// </summary>
public sealed class ChangeOwnPasswordRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The policy name registered in Program.cs and referenced by
    /// <c>[Authorize(Policy = ...)]</c> on the change-password action. Deliberately NOT a
    /// <see cref="PermissionCodes"/> value — the per-permission policies are registered by the
    /// <c>PermissionCodes.All</c> loop, and this one is registered separately because it is an OR
    /// of two conditions, not a single permission check.
    /// </summary>
    public const string PolicyName = "Auth.ChangePassword";
}
