using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Domain.Constants;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Api.Tests.Admin;

/// <summary>Covers ACTION_PLAN E11-01…E11-05 plus the coordinator-approved restore addition, over real HTTP.</summary>
public class AdminUsersEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public AdminUsersEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUser_AsAdmin_ReturnsCreated_WithTemporaryPasswordOnce_AndMustChangePasswordTrue()
    {
        var email = $"e2e-{Guid.NewGuid():N}@test.local";

        var response = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("E2E User", email, [_fixture.AssociateRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateUserResult>();
        body!.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        body.User.Email.Should().Be(email);
        body.User.MustChangePassword.Should().BeTrue();
        body.User.IsActive.Should().BeTrue();
        body.User.Roles.Should().Contain(RoleNames.Associate);

        // The temp password must actually work for the very first login.
        var loginResult = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), email, body.TemporaryPassword);
        loginResult.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task CreateUser_AsAssociate_Returns403()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("Should Not Exist", $"blocked-{Guid.NewGuid():N}@test.local", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_ReturnsCreatedUser_AndSupportsSearch()
    {
        var uniqueName = $"Findable-{Guid.NewGuid():N}"[..20];
        var email = $"findme-{Guid.NewGuid():N}@test.local";
        await _fixture.AdminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest(uniqueName, email, null));

        var response = await _fixture.AdminClient.GetAsync($"/api/v1/admin/users?search={Uri.EscapeDataString(uniqueName)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>();
        page!.Items.Should().ContainSingle(u => u.Email == email);
    }

    [Fact]
    public async Task ResetPassword_ReturnsNewTemporaryPasswordOnce_AndRevokesExistingRefreshTokens()
    {
        var email = $"reset-{Guid.NewGuid():N}@test.local";
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("Reset Target", email, [_fixture.AssociateRoleId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateUserResult>())!;

        // Log in once (as the still-must-change-password user) purely to obtain a refresh
        // token to prove it gets revoked — /auth/login is reachable even under the flag.
        var firstLogin = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), email, created.TemporaryPassword);

        var resetResponse = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{created.User.Id}/reset-password", content: null);

        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reset = await resetResponse.Content.ReadFromJsonAsync<ResetPasswordResult>();
        reset!.TemporaryPassword.Should().NotBe(created.TemporaryPassword);

        // The pre-reset refresh token must now be revoked (DR-10-style promptness).
        using var anonymousClient = _fixture.Factory.CreateClient();
        var refreshResponse = await anonymousClient.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(firstLogin.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The new temp password must work.
        var reLogin = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), email, reset.TemporaryPassword);
        reLogin.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_Returns404()
    {
        var response = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{Guid.NewGuid()}/reset-password", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetPassword_RevokesAlreadyIssuedAccessToken_SameTokenIsRejectedOnNextRequest()
    {
        // Task-1 follow-up: same proof shape as Deactivate_RevokesAlreadyIssuedAccessToken_...
        // below — E11-02 admin-forced reset now feeds the same ITokenRevocationService
        // deny-list, so an already-issued access token must stop working immediately, not
        // wait out its ~8h life.
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        using var associateClient = _fixture.Factory.CreateClient().WithBearer(associateAuth.AccessToken);
        (await associateClient.GetAsync("/api/v1/master-data")).StatusCode.Should().Be(HttpStatusCode.OK);

        // See the identical comment on the Deactivate/AssignRoles sibling tests: JWT `iat`
        // only has whole-second resolution, so this reflects realistic timing between a
        // login and the admin action revoking it.
        await Task.Delay(1100);

        var resetResponse = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{associateAuth.User.Id}/reset-password", content: null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResetResponse = await associateClient.GetAsync("/api/v1/master-data");
        afterResetResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        afterResetResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Deactivate_BlocksLogin_AndRevokesExistingRefreshTokens()
    {
        var email = $"deactivate-{Guid.NewGuid():N}@test.local";
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        // ProvisionActiveAssociateAsync creates its own user; grab its id via a fresh list search instead of re-deriving from auth (roles/permissions don't carry the id back conveniently here).
        var userId = associateAuth.User.Id;

        var deactivateResponse = await _fixture.AdminClient.DeleteAsync($"/api/v1/admin/users/{userId}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymousClient = _fixture.Factory.CreateClient();
        var refreshResponse = await anonymousClient.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(associateAuth.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // login must also now be blocked (AuthService.LoginAsync checks IsActive).
        var loginResponse = await anonymousClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(associateAuth.User.Email, "Associate-Changed-Pw1!"));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _ = email; // unused placeholder kept for symmetry with sibling tests
    }

    [Fact]
    public async Task Deactivate_RevokesAlreadyIssuedAccessToken_SameTokenIsRejectedOnNextRequest()
    {
        // ACTION_PLAN §10.2 N-7's exact reproduction: login and refresh already correctly
        // 401 after deactivation — this proves the previously-surviving gap (the
        // ALREADY-ISSUED access token) is now closed too, without waiting out its ~8h life.
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        using var associateClient = _fixture.Factory.CreateClient().WithBearer(associateAuth.AccessToken);

        // The token works before deactivation — any authenticated (non-admin) endpoint proves it.
        (await associateClient.GetAsync("/api/v1/master-data")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A JWT `iat` only has whole-second resolution (see TokenRevocationService's doc
        // comment on the hazard-1 fix), so a token minted and then revoked within the SAME
        // wall-clock second is not reliably distinguishable from a legitimate
        // revoke-then-reissue sequence — the design deliberately favors never self-locking-out
        // a freshly reissued token over catching that narrow same-second race. A real
        // deactivation is never seconds-close to the login it is revoking, so this delay
        // reflects realistic timing, not a workaround for a flaky assertion.
        await Task.Delay(1100);

        var deactivateResponse = await _fixture.AdminClient.DeleteAsync($"/api/v1/admin/users/{associateAuth.User.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // N-7: the SAME already-issued access token must now be rejected — 401, not 200.
        var afterDeactivateResponse = await associateClient.GetAsync("/api/v1/master-data");
        afterDeactivateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        afterDeactivateResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // Restore + a fresh login must work again (the deny-list must not outlive the revocation's purpose).
        var restoreResponse = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{associateAuth.User.Id}/restore", content: null);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reLogin = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), associateAuth.User.Email, "Associate-Changed-Pw1!");
        using var newClient = _fixture.Factory.CreateClient().WithBearer(reLogin.AccessToken);
        (await newClient.GetAsync("/api/v1/master-data")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivate_UnknownUser_Returns404()
    {
        var response = await _fixture.AdminClient.DeleteAsync($"/api/v1/admin/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivate_TheLastActiveSuperAdmin_Returns400()
    {
        // Deliberately does NOT assume "the bootstrap admin is the only Super Admin" —
        // AssignRoles_ChangesEffectivePermissions_...AndUnionsAdditively (a sibling test
        // sharing this same class fixture) intentionally leaves a second active Super Admin
        // behind, and xUnit does not guarantee method execution order within a class. Instead
        // this builds its own isolated precondition: create a dedicated Super Admin, then
        // directly force every OTHER Super Admin's IsActive to false (bypassing the guard,
        // exactly like a future milestone's direct DB access would), so the dedicated user is
        // PROVABLY the only active one regardless of what ran before it.
        var email = $"soloadmin-{Guid.NewGuid():N}@test.local";
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("Solo Admin", email, [_fixture.SuperAdminRoleId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateUserResult>())!;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherActiveSuperAdminIds = await db.Users
                .Include(u => u.UserRoles)
                .Where(u => u.Id != created.User.Id && u.IsActive && u.UserRoles.Any(ur => ur.RoleId == _fixture.SuperAdminRoleId))
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var id in otherActiveSuperAdminIds)
            {
                (await db.Users.FindAsync(id))!.IsActive = false;
            }
            await db.SaveChangesAsync();
        }

        var response = await _fixture.AdminClient.DeleteAsync($"/api/v1/admin/users/{created.User.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.FindAsync(created.User.Id))!.IsActive.Should().BeTrue("the rejected deactivation must not have partially applied");
        }
    }

    [Fact]
    public async Task Restore_ReactivatesADeactivatedUser_AndLoginWorksAgain()
    {
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        await _fixture.AdminClient.DeleteAsync($"/api/v1/admin/users/{associateAuth.User.Id}");

        var restoreResponse = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{associateAuth.User.Id}/restore", content: null);

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await restoreResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        restored!.IsActive.Should().BeTrue();

        var reLogin = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), associateAuth.User.Email, "Associate-Changed-Pw1!");
        reLogin.Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_UnknownUser_Returns404()
    {
        var response = await _fixture.AdminClient.PostAsync($"/api/v1/admin/users/{Guid.NewGuid()}/restore", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignRoles_ChangesEffectivePermissions_AndRevokesExistingRefreshTokens_AndUnionsAdditively()
    {
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        associateAuth.User.Permissions.Should().NotContain(PermissionCodes.AdminManageMasterData);

        var assignResponse = await _fixture.AdminClient.PutAsJsonAsync($"/api/v1/admin/users/{associateAuth.User.Id}/roles",
            new AssignRolesRequest([_fixture.AssociateRoleId, _fixture.SuperAdminRoleId]));

        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await assignResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        updated!.Roles.Should().BeEquivalentTo([RoleNames.Associate, RoleNames.SuperAdmin]);

        // DR-10: the pre-existing refresh token must be revoked immediately, not wait for expiry.
        using var anonymousClient = _fixture.Factory.CreateClient();
        var refreshResponse = await anonymousClient.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(associateAuth.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A fresh login must now carry the UNION of both roles' permissions (E3-02, end-to-end).
        var reLogin = await AdminApiTestHelpers.LoginAsync(_fixture.Factory.CreateClient(), associateAuth.User.Email, "Associate-Changed-Pw1!");
        reLogin.User.Roles.Should().BeEquivalentTo([RoleNames.Associate, RoleNames.SuperAdmin]);
        reLogin.User.Permissions.Should().Contain(PermissionCodes.AdminManageMasterData);
        reLogin.User.Permissions.Should().OnlyHaveUniqueItems("the union must not duplicate permissions shared by both roles");
    }

    [Fact]
    public async Task AssignRoles_RevokesAlreadyIssuedAccessToken_SameTokenIsRejectedOnNextRequest()
    {
        // N-7's second required trigger (ACTION_PLAN "must fire on deactivation and role
        // reassignment") — same proof shape as the deactivate test above, for role change.
        var associateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(_fixture.Factory, _fixture.AdminClient, _fixture.AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        using var associateClient = _fixture.Factory.CreateClient().WithBearer(associateAuth.AccessToken);
        (await associateClient.GetAsync("/api/v1/master-data")).StatusCode.Should().Be(HttpStatusCode.OK);

        // See the identical comment in the Deactivate test above: JWT `iat` only has
        // whole-second resolution, so this reflects realistic timing between a login and the
        // admin action revoking it, rather than racing the same wall-clock second.
        await Task.Delay(1100);

        var assignResponse = await _fixture.AdminClient.PutAsJsonAsync($"/api/v1/admin/users/{associateAuth.User.Id}/roles",
            new AssignRolesRequest([_fixture.AssociateRoleId]));
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResponse = await associateClient.GetAsync("/api/v1/master-data");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssignRoles_UnknownUser_Returns404()
    {
        var response = await _fixture.AdminClient.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/roles",
            new AssignRolesRequest([_fixture.AssociateRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListRoles_ReturnsSuperAdminAndAssociate()
    {
        var response = await _fixture.AdminClient.GetAsync("/api/v1/admin/roles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleDto>>();
        roles!.Select(r => r.Name).Should().Contain([RoleNames.SuperAdmin, RoleNames.Associate]);
    }
}
