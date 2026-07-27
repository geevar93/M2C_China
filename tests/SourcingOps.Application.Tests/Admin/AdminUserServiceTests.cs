using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Tests.Admin;

/// <summary>
/// Covers ACTION_PLAN E11-01…E11-05 plus the coordinator-approved restore addition.
/// </summary>
public class AdminUserServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static AdminUserService CreateSut(SourcingOps.Infrastructure.Persistence.AppDbContext db, out Mock<IAuditLogger> auditMock)
    {
        auditMock = new Mock<IAuditLogger>();
        return new AdminUserService(db, AuthTestData.RealPasswordHasher, auditMock.Object);
    }

    private static Role AddRole(SourcingOps.Infrastructure.Persistence.AppDbContext db, string name)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name };
        db.Roles.Add(role);
        db.SaveChanges();
        return role;
    }

    // ---- Create -------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ReturnsTemporaryPasswordOnce_AndSetsMustChangePasswordTrue()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(new CreateUserRequest("Jane Staff", "jane@example.com", [associate.Id]), Actor);

        result.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        result.TemporaryPassword.Length.Should().Be(12);
        result.User.MustChangePassword.Should().BeTrue();
        result.User.IsActive.Should().BeTrue();
        result.User.Roles.Should().Equal(RoleNames.Associate);

        var stored = db.Users.Single(u => u.Email == "jane@example.com");
        stored.PasswordHash.Should().NotBeNullOrWhiteSpace();
        // The generated password must actually verify against the stored hash — proves the
        // returned temp password is the real one, not a decoy.
        AuthTestData.RealPasswordHasher.Verify(stored, stored.PasswordHash, result.TemporaryPassword)
            .Should().NotBe(PasswordVerifyResult.Failed);
    }

    [Fact]
    public async Task CreateAsync_NeverLogsThePasswordItself()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        object? capturedDetails = null;
        audit.Setup(a => a.LogAsync(Actor, "UserCreated", "User", It.IsAny<string>(), It.IsAny<object>(), default))
            .Callback<Guid?, string, string, string?, object?, CancellationToken>((_, _, _, _, details, _) => capturedDetails = details)
            .Returns(Task.CompletedTask);

        var result = await sut.CreateAsync(new CreateUserRequest("Jane", "jane2@example.com", null), Actor);

        var serialized = System.Text.Json.JsonSerializer.Serialize(capturedDetails);
        serialized.Should().NotContain(result.TemporaryPassword);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(new CreateUserRequest("Jane", "dup@example.com", null), Actor);

        var act = async () => await sut.CreateAsync(new CreateUserRequest("Jane Two", "DUP@example.com", null), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownRoleId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var act = async () => await sut.CreateAsync(new CreateUserRequest("Jane", "jane3@example.com", [Guid.NewGuid()]), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Reset password -------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_IssuesNewPasswordAndRevokesExistingRefreshTokens()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane4@example.com", null), Actor);
        var user = db.Users.Single(u => u.Id == created.User.Id);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "hash1", ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await sut.ResetPasswordAsync(user.Id, Actor);

        result.Should().NotBeNull();
        result!.TemporaryPassword.Should().NotBe(created.TemporaryPassword);
        db.Users.Single(u => u.Id == user.Id).MustChangePassword.Should().BeTrue();
        db.RefreshTokens.Single(t => t.UserId == user.Id).RevokedAt.Should().NotBeNull();
        audit.Verify(a => a.LogAsync(Actor, "PasswordReset", "User", user.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownUser_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.ResetPasswordAsync(Guid.NewGuid(), Actor);

        result.Should().BeNull();
    }

    // ---- Deactivate (soft delete, E11-03/OI-2) --------------------------------

    [Fact]
    public async Task DeactivateAsync_SetsInactiveAndRevokesRefreshTokens()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane5@example.com", [associate.Id]), Actor);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = created.User.Id, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var found = await sut.DeactivateAsync(created.User.Id, Actor);

        found.Should().BeTrue();
        db.Users.Single(u => u.Id == created.User.Id).IsActive.Should().BeFalse();
        db.RefreshTokens.Single(t => t.UserId == created.User.Id).RevokedAt.Should().NotBeNull();
        audit.Verify(a => a.LogAsync(Actor, "UserDeactivated", "User", created.User.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_UnknownUser_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var found = await sut.DeactivateAsync(Guid.NewGuid(), Actor);

        found.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_AlreadyInactive_IsIdempotentAndReturnsTrue()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane6@example.com", [associate.Id]), Actor);
        await sut.DeactivateAsync(created.User.Id, Actor);
        audit.Invocations.Clear();

        var found = await sut.DeactivateAsync(created.User.Id, Actor);

        found.Should().BeTrue();
        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), default), Times.Never);
    }

    [Fact]
    public async Task DeactivateAsync_LastActiveSuperAdmin_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var superAdmin = AddRole(db, RoleNames.SuperAdmin);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(new CreateUserRequest("Only Admin", "onlyadmin@example.com", [superAdmin.Id]), Actor);

        var act = async () => await sut.DeactivateAsync(created.User.Id, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
        db.Users.Single(u => u.Id == created.User.Id).IsActive.Should().BeTrue("the rejected deactivation must not have partially applied");
    }

    [Fact]
    public async Task DeactivateAsync_SuperAdminWithAnotherActiveSuperAdmin_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var superAdmin = AddRole(db, RoleNames.SuperAdmin);
        var sut = CreateSut(db, out _);
        var first = await sut.CreateAsync(new CreateUserRequest("Admin One", "admin1@example.com", [superAdmin.Id]), Actor);
        var second = await sut.CreateAsync(new CreateUserRequest("Admin Two", "admin2@example.com", [superAdmin.Id]), Actor);

        var found = await sut.DeactivateAsync(first.User.Id, Actor);

        found.Should().BeTrue();
        db.Users.Single(u => u.Id == second.User.Id).IsActive.Should().BeTrue();
    }

    // ---- Restore (addition) ---------------------------------------------------

    [Fact]
    public async Task RestoreAsync_ReactivatesADeactivatedUser()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane7@example.com", [associate.Id]), Actor);
        await sut.DeactivateAsync(created.User.Id, Actor);

        var restored = await sut.RestoreAsync(created.User.Id, Actor);

        restored.Should().NotBeNull();
        restored!.IsActive.Should().BeTrue();
        audit.Verify(a => a.LogAsync(Actor, "UserRestored", "User", created.User.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_UnknownUser_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.RestoreAsync(Guid.NewGuid(), Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_AlreadyActive_IsIdempotentAndDoesNotDoubleAudit()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane8@example.com", [associate.Id]), Actor);

        var result = await sut.RestoreAsync(created.User.Id, Actor);

        result!.IsActive.Should().BeTrue();
        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), "UserRestored", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), default), Times.Never);
    }

    // ---- Assign roles (E11-05, DR-10) -------------------------------------------

    [Fact]
    public async Task AssignRolesAsync_ReplacesRolesAndRevokesRefreshTokens()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var superAdmin = AddRole(db, RoleNames.SuperAdmin);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane9@example.com", [associate.Id]), Actor);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = created.User.Id, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await sut.AssignRolesAsync(created.User.Id, [associate.Id, superAdmin.Id], Actor);

        result!.Roles.Should().BeEquivalentTo([RoleNames.Associate, RoleNames.SuperAdmin]);
        db.RefreshTokens.Single(t => t.UserId == created.User.Id).RevokedAt.Should().NotBeNull();
        audit.Verify(a => a.LogAsync(Actor, "UserRolesChanged", "User", created.User.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task AssignRolesAsync_UnknownUser_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.AssignRolesAsync(Guid.NewGuid(), [], Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AssignRolesAsync_UnknownRoleId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane10@example.com", [associate.Id]), Actor);

        var act = async () => await sut.AssignRolesAsync(created.User.Id, [Guid.NewGuid()], Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task AssignRolesAsync_RemovingLastSuperAdminsSuperAdminRole_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var superAdmin = AddRole(db, RoleNames.SuperAdmin);
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(new CreateUserRequest("Only Admin", "onlyadmin2@example.com", [superAdmin.Id]), Actor);

        var act = async () => await sut.AssignRolesAsync(created.User.Id, [associate.Id], Actor);

        await act.Should().ThrowAsync<AppValidationException>();
        db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Single(u => u.Id == created.User.Id)
            .UserRoles.Should().ContainSingle(ur => ur.Role.Name == RoleNames.SuperAdmin);
    }

    // ---- Additive role resolution end-to-end (E3-02) ---------------------------

    [Fact]
    public async Task AssignRolesAsync_TwoOverlappingRoles_ResultIsTheUnion_NoDuplicates()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var superAdmin = AddRole(db, RoleNames.SuperAdmin);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(new CreateUserRequest("Jane", "jane11@example.com", [associate.Id]), Actor);

        var result = await sut.AssignRolesAsync(created.User.Id, [associate.Id, superAdmin.Id, associate.Id], Actor);

        result!.Roles.Should().BeEquivalentTo([RoleNames.Associate, RoleNames.SuperAdmin]);
        result.Roles.Should().OnlyHaveUniqueItems();
    }

    // ---- List / search / paging (E11-04) ----------------------------------------

    [Fact]
    public async Task ListAsync_ExcludesInactiveByDefault_IncludesWhenRequested()
    {
        using var db = TestDbContextFactory.Create();
        var associate = AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out _);
        var active = await sut.CreateAsync(new CreateUserRequest("Active One", "active@example.com", [associate.Id]), Actor);
        var toDeactivate = await sut.CreateAsync(new CreateUserRequest("Inactive One", "inactive@example.com", [associate.Id]), Actor);
        await sut.DeactivateAsync(toDeactivate.User.Id, Actor);

        var defaultList = await sut.ListAsync(search: null, page: 1, pageSize: 20, includeInactive: false);
        var fullList = await sut.ListAsync(search: null, page: 1, pageSize: 20, includeInactive: true);

        defaultList.Items.Should().ContainSingle(u => u.Id == active.User.Id);
        defaultList.Items.Should().NotContain(u => u.Id == toDeactivate.User.Id);
        fullList.Items.Should().Contain(u => u.Id == toDeactivate.User.Id);
    }

    [Fact]
    public async Task ListAsync_SearchMatchesNameOrEmail_CaseInsensitive()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(new CreateUserRequest("Priya Sharma", "priya@example.com", null), Actor);
        await sut.CreateAsync(new CreateUserRequest("Someone Else", "someone@example.com", null), Actor);

        var result = await sut.ListAsync(search: "PRIYA", page: 1, pageSize: 20, includeInactive: false);

        result.Items.Should().ContainSingle(u => u.Email == "priya@example.com");
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateAsync(new CreateUserRequest($"User {i}", $"user{i}@example.com", null), Actor);
        }

        var page1 = await sut.ListAsync(search: null, page: 1, pageSize: 2, includeInactive: false);
        var page2 = await sut.ListAsync(search: null, page: 2, pageSize: 2, includeInactive: false);

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.Items.Select(u => u.Id).Should().NotIntersectWith(page2.Items.Select(u => u.Id));
    }

    // ---- List roles ---------------------------------------------------------------

    [Fact]
    public async Task ListRolesAsync_ReturnsAllRolesOrderedByName()
    {
        using var db = TestDbContextFactory.Create();
        AddRole(db, RoleNames.SuperAdmin);
        AddRole(db, RoleNames.Associate);
        var sut = CreateSut(db, out _);

        var roles = await sut.ListRolesAsync();

        roles.Select(r => r.Name).Should().Equal(RoleNames.Associate, RoleNames.SuperAdmin);
    }
}
