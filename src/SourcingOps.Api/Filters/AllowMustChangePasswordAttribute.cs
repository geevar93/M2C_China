namespace SourcingOps.Api.Filters;

/// <summary>
/// Marks an authenticated action as reachable even while the caller's token has
/// `must_change_password=true`. Only the change-password endpoint carries this
/// (TECH_SPEC §4.2, E1-08). Logout and the health endpoint don't need it — they're
/// `[AllowAnonymous]`, which <see cref="RequirePasswordChangeFilter"/> already skips.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowMustChangePasswordAttribute : Attribute;
