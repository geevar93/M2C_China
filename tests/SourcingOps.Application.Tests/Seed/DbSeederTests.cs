using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence.Seed;

namespace SourcingOps.Application.Tests.Seed;

/// <summary>
/// Covers ACTION_PLAN E1-03's explicit "re-running is idempotent" acceptance criterion.
///
/// <para>
/// <b>THE RULE IN THIS FILE (ACTION_PLAN N-12):</b> a test may reference a constant to prove
/// <i>behaviour</i>, but never to prove <i>identity</i>. An identity assertion terminates in a
/// literal or it proves nothing.
/// </para>
/// <para>
/// Every "the seeded set is X" assertion below therefore spells X out. Reading it back from
/// <c>PermissionCodes</c>/<c>SeedDefaults</c> — which is what the seeder wrote in the first
/// place — is a round trip that passes no matter what those constants say, including after a
/// rename that breaks every <c>[Authorize]</c> policy and every client permission guard. That
/// is not hypothetical: <b>D-50 was exactly this failure</b>, where the whole client suite
/// stayed green through a live functional break because its fixtures encoded the same wrong
/// value the production code used.
/// </para>
/// <para>
/// The idempotency and preservation assertions are the opposite case and correctly reference
/// the constants: there the constant is an <i>input</i> to the behaviour under test (seed
/// twice, assert no duplication), not the answer being checked.
/// </para>
/// <para>
/// Duplication here is the point. When one of these lists changes, a test must fail and a
/// human must confirm the change was intended — for permission codes especially, since a
/// silent divergence there is an authorisation hole, not a rendering bug.
/// </para>
/// </summary>
public class DbSeederTests
{
    /// <summary>
    /// The full permission catalogue, as literals. Deliberately NOT
    /// <c>PermissionCodes.All</c> — see the rule in this class's summary. These strings cross
    /// three boundaries (seeder → <c>[Authorize(Policy=…)]</c> → the client's
    /// <c>permissionGuard</c> and nav visibility), and nothing else in the suite pins them.
    /// </summary>
    private static readonly string[] ExpectedPermissionCodes =
    [
        "Customers.View", "Customers.Edit",
        "Vendors.View", "Vendors.Edit",
        "Catalogs.View", "Catalogs.Edit",
        "Inventory.View", "Inventory.Edit", "Inventory.Adjust",
        "Shipments.View", "Shipments.Edit",
        "Invoicing.View", "Invoicing.Edit", "Invoicing.MarkPaid",
        "Dispatch.Send",
        "Analytics.View",
        "Admin.ManageUsers", "Admin.ManageMasterData",
        // E11-10. Added deliberately, per this class's "a human must confirm the change" rule:
        // it gates the VOLUNTARY self-service path through POST /auth/change-password and is in
        // PermissionCodes.AdminOnly, so it must appear for SuperAdmin and NOT for Associate
        // (asserted below). The forced-change path does not depend on it — see
        // ChangeOwnPasswordAuthorizationHandler's second branch.
        "Account.ChangeOwnPassword"
    ];

    private static DbSeeder CreateSut(SourcingOps.Infrastructure.Persistence.AppDbContext db, BootstrapAdminOptions? options = null) =>
        new(db, AuthTestData.RealPasswordHasher, options ?? new BootstrapAdminOptions(), NullLogger<DbSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_CreatesFullPermissionCatalog()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        db.Permissions.Select(p => p.Code).Should().BeEquivalentTo(ExpectedPermissionCodes);
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

        superAdminCodes.Should().BeEquivalentTo(ExpectedPermissionCodes);

        // Spelled out rather than `All.Except(AdminOnly)`: deriving the expectation from
        // AdminOnly is exactly what would let AdminOnly silently shrink. If someone removes
        // Admin.ManageUsers from that array, the derived version happily asserts that
        // Associates SHOULD hold it — and passes.
        associateCodes.Should().BeEquivalentTo(new[]
        {
            "Customers.View", "Customers.Edit",
            "Vendors.View", "Vendors.Edit",
            "Catalogs.View", "Catalogs.Edit",
            "Inventory.View", "Inventory.Edit", "Inventory.Adjust",
            "Shipments.View", "Shipments.Edit",
            "Invoicing.View", "Invoicing.Edit", "Invoicing.MarkPaid",
            "Dispatch.Send",
            "Analytics.View"
        });
        associateCodes.Should().NotContain("Admin.ManageUsers");
        associateCodes.Should().NotContain("Admin.ManageMasterData");
        associateCodes.Should().NotContain("Account.ChangeOwnPassword",
            "E11-10 restricts the voluntary self-service password change to Super Admins; an Associate reaches "
            + "/auth/change-password only while its own must_change_password flag is set");
    }

    /// <summary>
    /// ACTION_PLAN E9-08: FSD Q4 ("any staff may dispatch over WhatsApp") was answered and
    /// recorded in TECH_SPEC §10 OI-8 as "matches the seeded Associate role already holding
    /// Dispatch.Send, no change". The test above already covers this indirectly (Dispatch.Send
    /// is in <see cref="PermissionCodes.All"/> and not in <see cref="PermissionCodes.AdminOnly"/>,
    /// so it flows into "everything except AdminOnly") — but that assertion is derived from the
    /// same <see cref="PermissionCodes.AdminOnly"/> list it's checking against, so it would keep
    /// passing even if a future change moved <c>Dispatch.Send</c> into <c>AdminOnly</c> (the
    /// expected set would silently shrink along with the actual one). This test hardcodes the
    /// answered business rule directly so that specific regression cannot pass silently.
    /// </summary>
    [Fact]
    public async Task SeedAsync_AssociateRole_HoldsDispatchSend_PerFsdQ4AnsweredInOI8()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        var associate = db.Roles.Single(r => r.Name == RoleNames.Associate);
        var associateCodes = db.RolePermissions.Where(rp => rp.RoleId == associate.Id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code).ToList();

        associateCodes.Should().Contain(PermissionCodes.DispatchSend,
            "FSD Q4 was answered as 'any staff may dispatch' (TECH_SPEC OI-8) — the seeded Associate role must keep Dispatch.Send regardless of how AdminOnly is defined");
    }

    [Fact]
    public async Task SeedAsync_CreatesTheSixCategoriesAndBothServiceTypesAndDefaultStatusLookups()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db);

        await sut.SeedAsync();

        // All literals — see the class summary. Notably `FREIGHT_ONLY`: the previous version of
        // this line read it back from SeedDefaults, so renaming the code would have kept it
        // green while breaking D-36's freight-only branch server-side and the chip map, the
        // intake form's FSD Q1 fields and the stock-impact column client-side.
        db.Categories.Select(c => c.Name).Should()
            .BeEquivalentTo("Jewellery", "Furniture", "Stationery", "Handbags", "Electronics", "Tools");
        db.ServiceTypes.Select(s => s.Code).Should().BeEquivalentTo("CIF", "FREIGHT_ONLY");
        db.LeadStatuses.Select(s => s.Code).Should()
            .BeEquivalentTo("NEW", "QUALIFIED", "ACTIVE", "WON", "LOST", "DORMANT");
        // "IN TRANSIT" carries a literal space, matching the prototype's ST map key verbatim.
        // Asserting it here is the only place that spelling is pinned.
        db.ShipmentStatuses.Select(s => s.Code).Should()
            .BeEquivalentTo("PACKED", "DISPATCHED", "IN TRANSIT", "DELIVERED");
        db.InvoiceStatuses.Select(s => s.Code).Should()
            .BeEquivalentTo("DRAFT", "ISSUED", "PAID", "CANCELLED");
        db.VendorStatuses.Select(s => s.Code).Should()
            .BeEquivalentTo("ACTIVE", "ON-HOLD", "INACTIVE");
    }

    /// <summary>
    /// E11-10 DEVIATION, recorded deliberately. This test previously asserted
    /// <c>MustChangePassword == true</c> for a CONFIGURED password. It now asserts false, and the
    /// generated-password case below asserts true. The flag was split because the two cases carry
    /// different risk: a generated password is unknown until a human reads it out of an
    /// application log, and a secret written to a log must be rotated — so the forced change stays
    /// exactly where that exposure is. An explicitly configured password is a chosen credential;
    /// forcing an immediate change would make the configured value unusable as configured, which
    /// is the whole point of configuring it. Both branches are pinned so neither can drift.
    /// </summary>
    [Fact]
    public async Task SeedAsync_WithConfiguredPassword_CreatesExactlyOneBootstrapSuperAdmin_WithMustChangePasswordFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, new BootstrapAdminOptions { AdminEmail = "owner@example.com", AdminPassword = "Provided-Pw1" });

        await sut.SeedAsync();

        var admin = db.Users.Single(u => u.Email == "owner@example.com");
        admin.MustChangePassword.Should().BeFalse(
            "a password the operator explicitly configured is meant to be usable exactly as configured");
        admin.IsActive.Should().BeTrue();

        var role = db.Roles.Single(r => r.Name == RoleNames.SuperAdmin);
        db.UserRoles.Should().ContainSingle(ur => ur.UserId == admin.Id && ur.RoleId == role.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedAsync_WithNoConfiguredPassword_GeneratesOne_AndForcesAChangeOnFirstLogin(string? configured)
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, new BootstrapAdminOptions { AdminEmail = "owner@example.com", AdminPassword = configured });

        await sut.SeedAsync();

        var admin = db.Users.Single(u => u.Email == "owner@example.com");
        admin.MustChangePassword.Should().BeTrue(
            "a generated password is logged in cleartext once, and a secret that reached a log must be rotated");
        admin.IsActive.Should().BeTrue();
        admin.PasswordHash.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The shipped defaults are the credential the owner is told to sign in with, so they are
    /// pinned as literals here rather than read back off <see cref="BootstrapAdminOptions"/>
    /// (that would be the round trip this class's summary warns about). Note these defaults only
    /// apply where nothing binds over them — appsettings.json and docker-compose.yml carry the
    /// same pair explicitly, because Bind overwrites with an empty string.
    /// </summary>
    [Fact]
    public async Task SeedAsync_WithStockOptions_SeedsTheDocumentedOwnerCredential()
    {
        using var db = TestDbContextFactory.Create();
        var options = new BootstrapAdminOptions();
        options.AdminEmail.Should().Be("owner@sourcingops.local");
        options.AdminPassword.Should().Be("Welcome@123");

        await CreateSut(db, options).SeedAsync();

        var admin = db.Users.Single(u => u.Email == "owner@sourcingops.local");
        admin.MustChangePassword.Should().BeFalse();
        AuthTestData.RealPasswordHasher.Verify(admin, admin.PasswordHash, "Welcome@123")
            .Should().Be(PasswordVerifyResult.Success);
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

        // Literal counts, not `.Length` off the source array: the assertion here is "seeding
        // twice did not duplicate", and a count derived from the seeder's own input cannot
        // distinguish "17 rows, correct" from "17 rows, wrong set".
        db.Permissions.Select(p => p.Code).Should().BeEquivalentTo(ExpectedPermissionCodes); // no duplicates
        db.Permissions.Should().HaveCount(19);
        db.Roles.Select(r => r.Name).Should().BeEquivalentTo(RoleNames.All);
        db.Categories.Count().Should().Be(6);
        db.Users.Count(u => u.Email == "owner@example.com").Should().Be(1);
        db.Users.Single(u => u.Email == "owner@example.com").Id.Should().Be(firstRunAdminId);
        db.Users.Single(u => u.Email == "owner@example.com").PasswordHash.Should().Be(firstRunPasswordHash);
    }
}
