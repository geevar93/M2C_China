using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Auth;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.TestSupport;

public static class AuthTestData
{
    public static readonly IPasswordHasher RealPasswordHasher = new PasswordHasherAdapter();

    /// <summary>Creates a role with the given permission codes and an active user holding it, persisted into <paramref name="db"/>.</summary>
    public static User CreateActiveUserWithRole(AppDbContext db, string roleName, string password, params string[] permissionCodes)
    {
        var permissions = permissionCodes.Select(code => new Permission { Id = Guid.NewGuid(), Code = code }).ToList();
        db.Permissions.AddRange(permissions);

        var role = new Role { Id = Guid.NewGuid(), Name = roleName };
        db.Roles.Add(role);

        foreach (var permission in permissions)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Test User",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = RealPasswordHasher.Hash(user, password);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        db.SaveChanges();
        return user;
    }
}
