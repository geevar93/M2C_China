using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Admin;

/// <summary>
/// See <see cref="IAdminUserService"/> for the story mapping. One deliberate addition beyond
/// the written stories: deactivating (E11-03) or reassigning roles away from (E11-05) the
/// LAST active Super Admin is rejected with a validation error. Neither story asks for this,
/// but OI-2's soft-delete decision already accepts that reversal requires another Super Admin
/// to act — if that were ever zero, the account tree would need direct DB intervention to
/// recover. Rejecting the action that would create that state is cheap now and expensive to
/// retrofit later, so it is included and flagged here rather than silently absorbed.
/// </summary>
public sealed class AdminUserService : IAdminUserService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogger _audit;

    public AdminUserService(IAppDbContext db, IPasswordHasher passwordHasher, IAuditLogger audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<PagedResult<AdminUserDto>> ListAsync(string? search, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

        var query = _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // .ToLower().Contains(...) rather than a provider-specific ILIKE: this method
            // must translate identically against both the real Npgsql provider and the
            // EF Core InMemory provider used by the Application-layer unit tests.
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Name.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = users.Select(u => ToDto(u)).ToList();
        return new PagedResult<AdminUserDto>(items, totalCount, page, pageSize);
    }

    public async Task<CreateUserResult> CreateAsync(CreateUserRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new AppValidationException("email", "A valid email is required.");
        }

        var emailTaken = await _db.Users.AnyAsync(u => u.Email == email, ct);
        if (emailTaken)
        {
            throw new AppValidationException("email", $"A user with email '{email}' already exists.");
        }

        var roles = await ResolveRolesAsync(request.RoleIds ?? [], ct);

        var temporaryPassword = TempPasswordGenerator.Generate();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.Hash(user, temporaryPassword);
        _db.Users.Add(user);

        foreach (var role in roles)
        {
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        await _db.SaveChangesAsync(ct);

        // Never include the password itself in the audit trail — only that an account was
        // created and with which roles.
        await _audit.LogAsync(actorUserId, "UserCreated", "User", user.Id.ToString(), new { user.Email, Roles = roles.Select(r => r.Name) }, ct);

        var roleNames = roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return new CreateUserResult(ToDto(user, roleNames), temporaryPassword);
    }

    public async Task<ResetPasswordResult?> ResetPasswordAsync(Guid userId, Guid actorUserId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return null;
        }

        var temporaryPassword = TempPasswordGenerator.Generate();
        user.PasswordHash = _passwordHasher.Hash(user, temporaryPassword);
        user.MustChangePassword = true;

        await RevokeActiveRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "PasswordReset", "User", user.Id.ToString(), null, ct);
        return new ResetPasswordResult(temporaryPassword);
    }

    public async Task<bool> DeactivateAsync(Guid userId, Guid actorUserId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return false;
        }

        if (!user.IsActive)
        {
            return true; // already deactivated — idempotent
        }

        if (user.UserRoles.Any(ur => ur.Role.Name == RoleNames.SuperAdmin))
        {
            var hasAnotherActiveSuperAdmin = await AnyOtherActiveSuperAdminAsync(userId, ct);
            if (!hasAnotherActiveSuperAdmin)
            {
                throw new AppValidationException("id", "Cannot deactivate the last active Super Admin account.");
            }
        }

        user.IsActive = false;
        await RevokeActiveRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "UserDeactivated", "User", user.Id.ToString(), null, ct);
        return true;
    }

    public async Task<AdminUserDto?> RestoreAsync(Guid userId, Guid actorUserId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "UserRestored", "User", user.Id.ToString(), null, ct);
        }

        return ToDto(user);
    }

    public async Task<AdminUserDto?> AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, Guid actorUserId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var roles = await ResolveRolesAsync(roleIds, ct);

        var currentlyHasSuperAdmin = user.UserRoles.Any(ur => ur.Role.Name == RoleNames.SuperAdmin);
        var willHaveSuperAdmin = roles.Any(r => r.Name == RoleNames.SuperAdmin);

        if (user.IsActive && currentlyHasSuperAdmin && !willHaveSuperAdmin)
        {
            var hasAnotherActiveSuperAdmin = await AnyOtherActiveSuperAdminAsync(userId, ct);
            if (!hasAnotherActiveSuperAdmin)
            {
                throw new AppValidationException("roleIds", "Cannot remove the Super Admin role from the last active Super Admin.");
            }
        }

        foreach (var link in user.UserRoles.ToList())
        {
            _db.UserRoles.Remove(link);
        }
        foreach (var role in roles)
        {
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        await RevokeActiveRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        var roleNames = roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        await _audit.LogAsync(actorUserId, "UserRolesChanged", "User", user.Id.ToString(), new { Roles = roleNames }, ct);

        return ToDto(user, roleNames);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.OrderBy(r => r.Name).ToListAsync(ct);
        return roles.Select(r => new RoleDto(r.Id, r.Name)).ToList();
    }

    private async Task<List<Role>> ResolveRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        var distinctIds = roleIds.Distinct().ToList();
        var roles = await _db.Roles.Where(r => distinctIds.Contains(r.Id)).ToListAsync(ct);

        if (roles.Count != distinctIds.Count)
        {
            var foundIds = roles.Select(r => r.Id).ToHashSet();
            var unknown = distinctIds.Where(id => !foundIds.Contains(id));
            throw new AppValidationException("roleIds", $"Unknown role id(s): {string.Join(", ", unknown)}.");
        }

        return roles;
    }

    private async Task<bool> AnyOtherActiveSuperAdminAsync(Guid excludeUserId, CancellationToken ct)
    {
        var others = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.Id != excludeUserId && u.IsActive)
            .ToListAsync(ct);

        return others.Any(u => u.UserRoles.Any(ur => ur.Role.Name == RoleNames.SuperAdmin));
    }

    private async Task RevokeActiveRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }
    }

    private static AdminUserDto ToDto(User user, IReadOnlyList<string> roles) =>
        new(user.Id, user.Name, user.Email, user.IsActive, user.MustChangePassword, roles, user.CreatedAt);

    private static AdminUserDto ToDto(User user) =>
        ToDto(user, user.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());
}
