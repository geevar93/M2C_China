using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence.Seed;

namespace SourcingOps.Application.Tests.Seed;

/// <summary>Covers ACTION_PLAN E1-03's explicit "re-running is idempotent" acceptance criterion.</summary>
public class DbSeederTests
{
    private static DbSeeder CreateSut(SourcingOps.Infrastructure.Persistence.AppDbContext db, BootstrapAdminOptions? options = null) =>
        new(db, AuthTestData.RealPasswordHasher, options ?? new BootstrapAdminOptions(), NullLogger<DbSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_CreatesFullPermissionCatalog()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        db.Permissions.Select(p => p.Code).Should().BeEquivalentTo(PermissionCodes.All);
    }

    [Fact]
    public async Task SeedAsync_GrantsSuperAdminEveryPermission_AndAssociateEverythingExceptAdminOnly()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        var superAdmin = db.Roles.Single(r => r.Name == RoleNames.SuperAdmin);
        var associate = db.Roles.Single(r => r.Name == RoleNames.Associate);

        var superAdminCodes = db.RolePermissions.Where(rp => rp.RoleId == superAdmin.Id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code).ToList();
        var associateCodes = db.RolePermissions.Where(rp => rp.RoleId == associate.Id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code).ToList();

        superAdminCodes.Should().BeEquivalentTo(PermissionCodes.All);
        associateCodes.Should().BeEquivalentTo(PermissionCodes.All.Except(PermissionCodes.AdminOnly));
        associateCodes.Should().NotContain(PermissionCodes.AdminOnly);
    }

    [Fact]
    public async Task SeedAsync_CreatesTheSixCategoriesAndBothServiceTypesAndDefaultStatusLookups()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        db.Categories.Select(c => c.Name).Should().BeEquivalentTo(SeedDefaults.Categories.Select(c => c.Name));
        db.ServiceTypes.Select(s => s.Code).Should().BeEquivalentTo([SeedDefaults.ServiceTypeCif, SeedDefaults.ServiceTypeFreightOnly]);
        db.LeadStatuses.Select(s => s.Code).Should().BeEquivalentTo(SeedDefaults.LeadStatuses.Select(s => s.Code));
        db.ShipmentStatuses.Select(s => s.Code).Should().BeEquivalentTo(SeedDefaults.ShipmentStatuses.Select(s => s.Code));
        db.InvoiceStatuses.Select(s => s.Code).Should().BeEquivalentTo(SeedDefaults.InvoiceStatuses.Select(s => s.Code));
        db.VendorStatuses.Select(s => s.Code).Should().BeEquivalentTo(SeedDefaults.VendorStatuses.Select(s => s.Code));
    }

    [Fact]
    public async Task SeedAsync_CreatesExactlyOneBootstrapSuperAdmin_WithMustChangePasswordTrue()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, new BootstrapAdminOptions { AdminEmail = "owner@example.com", AdminPassword = "Provided-Pw1" });

        await sut.SeedAsync();

        var admin = db.Users.Single(u => u.Email == "owner@example.com");
        admin.MustChangePassword.Should().BeTrue();
        admin.IsActive.Should().BeTrue();

        var role = db.Roles.Single(r => r.Name == RoleNames.SuperAdmin);
        db.UserRoles.Should().ContainSingle(ur => ur.UserId == admin.Id && ur.RoleId == role.Id);
    }

    [Fact]
    public async Task SeedAsync_MarksEverySeededDefaultRowAsSystemDefault()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        db.Categories.Should().OnlyContain(c => c.IsSystemDefault);
        db.ServiceTypes.Should().OnlyContain(s => s.IsSystemDefault);
        db.LeadStatuses.Should().OnlyContain(s => s.IsSystemDefault);
        db.ShipmentStatuses.Should().OnlyContain(s => s.IsSystemDefault);
        db.InvoiceStatuses.Should().OnlyContain(s => s.IsSystemDefault);
        db.VendorStatuses.Should().OnlyContain(s => s.IsSystemDefault);
    }

    [Fact]
    public async Task SeedAsync_SelfHealsIsSystemDefault_OnARowThatPreDatesTheFlag()
    {
        // ACTION_PLAN N-8: an existing database's seeded rows (created before IsSystemDefault
        // existed, or somehow reset) must pick the flag back up on the next startup without a
        // one-off data migration — DbSeeder's normal idempotent "fill gaps" pass self-heals it.
        using var db = TestDbContextFactory.Create();
        db.VendorStatuses.Add(new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1, IsSystemDefault = false });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        db.VendorStatuses.Single(s => s.Code == "ACTIVE").IsSystemDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_SelfHeal_DoesNotTouchIsActiveOrSortOrder_OfAPreExistingRow()
    {
        // The self-heal must be scoped to IsSystemDefault only — a Super Admin's own
        // retire/reorder choices on a default row (E3-08's normal, still-supported retire
        // path) must survive re-seeding, not get silently reset.
        using var db = TestDbContextFactory.Create();
        db.VendorStatuses.Add(new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = false, SortOrder = 99, IsSystemDefault = false });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        var row = db.VendorStatuses.Single(s => s.Code == "ACTIVE");
        row.IsSystemDefault.Should().BeTrue();
        row.IsActive.Should().BeFalse("a prior retire decision must not be reverted by re-seeding");
        row.SortOrder.Should().Be(99, "a prior reorder decision must not be reverted by re-seeding");
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateAnyRowsAndKeepsTheSameBootstrapUser()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, new BootstrapAdminOptions { AdminEmail = "owner@example.com", AdminPassword = "Provided-Pw1" });

        await sut.SeedAsync();
        var firstRunAdminId = db.Users.Single(u => u.Email == "owner@example.com").Id;
        var firstRunPasswordHash = db.Users.Single(u => u.Email == "owner@example.com").PasswordHash;

        // Re-run with a DIFFERENT configured password — since the account already
        // exists, the seeder must not reset it (never reprint/reset per its doc comment).
        var secondSut = CreateSut(db, new BootstrapAdminOptions { AdminEmail = "owner@example.com", AdminPassword = "Different-Pw2" });
        await secondSut.SeedAsync();

        db.Permissions.Select(p => p.Code).Should().BeEquivalentTo(PermissionCodes.All); // no duplicates
        db.Roles.Select(r => r.Name).Should().BeEquivalentTo(RoleNames.All);
        db.Categories.Count().Should().Be(SeedDefaults.Categories.Length);
        db.Users.Count(u => u.Email == "owner@example.com").Should().Be(1);
        db.Users.Single(u => u.Email == "owner@example.com").Id.Should().Be(firstRunAdminId);
        db.Users.Single(u => u.Email == "owner@example.com").PasswordHash.Should().Be(firstRunPasswordHash);
    }
}
