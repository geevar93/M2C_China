using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Auth;

/// <summary>
/// Resolves a user's effective permission set as the union of their roles' permissions
/// (FSD §4/A5, TECH_SPEC §4.3 "roles are additive"). Phase 1 seeds one role per user,
/// but this already implements the multi-role union — pulled forward from ACTION_PLAN
/// E3-02 since it falls out for free while building login.
/// </summary>
public static class PermissionResolver
{
    public static IReadOnlyList<string> Resolve(IEnumerable<Role> roles)
    {
        return roles
            .SelectMany(r => r.RolePermissions.Select(rp => rp.Permission.Code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> RoleNames(IEnumerable<Role> roles) =>
        roles.Select(r => r.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
}
