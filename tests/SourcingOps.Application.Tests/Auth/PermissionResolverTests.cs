using FluentAssertions;
using SourcingOps.Application.Auth;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Tests.Auth;

/// <summary>
/// Covers ACTION_PLAN E3-02 (additive role resolution), pulled forward into E1-07's
/// login path since it falls out for free — see AuthService/PermissionResolver doc comments.
/// </summary>
public class PermissionResolverTests
{
    private static Role RoleWith(string name, params string[] permissionCodes)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name };
        foreach (var code in permissionCodes)
        {
            var permission = new Permission { Id = Guid.NewGuid(), Code = code };
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Role = role, PermissionId = permission.Id, Permission = permission });
        }

        return role;
    }

    [Fact]
    public void Resolve_WithMultipleRoles_ReturnsUnionOfPermissions()
    {
        var roleA = RoleWith("A", "Customers.View", "Customers.Edit");
        var roleB = RoleWith("B", "Customers.Edit", "Vendors.View"); // overlapping + distinct

        var result = PermissionResolver.Resolve([roleA, roleB]);

        result.Should().BeEquivalentTo(["Customers.View", "Customers.Edit", "Vendors.View"]);
    }

    [Fact]
    public void Resolve_WithNoRoles_ReturnsEmpty()
    {
        var result = PermissionResolver.Resolve([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void RoleNames_ReturnsDistinctSortedNames()
    {
        var roleA = RoleWith("SuperAdmin");
        var roleB = RoleWith("Associate");

        var result = PermissionResolver.RoleNames([roleA, roleB, roleA]);

        result.Should().Equal("Associate", "SuperAdmin");
    }
}
