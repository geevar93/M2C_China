namespace SourcingOps.Application.Admin;

public sealed record CreateUserRequest(string Name, string Email, IReadOnlyList<Guid>? RoleIds);

public sealed record AdminUserDto(
    Guid Id,
    string Name,
    string Email,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt);

/// <summary>
/// <see cref="TemporaryPassword"/> is returned exactly once, in this response body — it is
/// never logged (see <see cref="AdminUserService"/>'s audit calls, which omit it), never
/// stored in plaintext, and never echoed back by any read endpoint (TECH_SPEC §4.4).
/// </summary>
public sealed record CreateUserResult(AdminUserDto User, string TemporaryPassword);

/// <summary>Same one-time-display rule as <see cref="CreateUserResult.TemporaryPassword"/>.</summary>
public sealed record ResetPasswordResult(string TemporaryPassword);

public sealed record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record RoleDto(Guid Id, string Name);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
