using Microsoft.AspNetCore.Authorization;

namespace SourcingOps.Api.Authorization;

/// <summary>
/// TECH_SPEC §4.3 / ACTION_PLAN E3-01: backs every <c>[Authorize(Policy = "Customers.Edit")]</c>-
/// style attribute. One requirement per permission code — Program.cs registers a policy for
/// every entry in <see cref="SourcingOps.Domain.Constants.PermissionCodes.All"/>, so adding a
/// new permission is a one-line catalog addition, not new plumbing.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
