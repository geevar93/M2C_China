namespace SourcingOps.Application.Admin;

/// <summary>
/// Super-Admin-only account management (TECH_SPEC §4.4; ACTION_PLAN E11-01…E11-05, plus the
/// coordinator-approved <c>RestoreAsync</c> addition — E11-03 is the only mutation in this
/// module with no reverse operation otherwise). Every mutation writes through
/// <see cref="Interfaces.IAuditLogger"/>; password-affecting and role-affecting mutations
/// revoke the target user's existing refresh tokens (DR-10) so the change takes effect
/// promptly instead of waiting out the access token's ~8h lifetime.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserDto>> ListAsync(string? search, int page, int pageSize, bool includeInactive, CancellationToken ct = default);

    Task<CreateUserResult> CreateAsync(CreateUserRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null if the user does not exist.</summary>
    Task<ResetPasswordResult?> ResetPasswordAsync(Guid userId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Soft delete (E11-03/OI-2). False if the user does not exist.</summary>
    Task<bool> DeactivateAsync(Guid userId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Addition beyond the written stories — see build report. Null if the user does not exist.</summary>
    Task<AdminUserDto?> RestoreAsync(Guid userId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null if the user does not exist.</summary>
    Task<AdminUserDto?> AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);
}
