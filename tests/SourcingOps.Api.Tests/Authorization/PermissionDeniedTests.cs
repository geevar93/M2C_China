using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Application.MasterData;

namespace SourcingOps.Api.Tests.Authorization;

/// <summary>
/// Closes ACTION_PLAN §9.2 item N-2: M1 could not verify either 403 path end-to-end because
/// no permission-gated endpoint existed yet (a live attempt returned 404). Now that
/// MasterDataController/AdminUsersController exist (E3-01/E3-08/E11-*), both paths are
/// exercised for real:
///   (a) a caller lacking the required permission gets 403 (Associate hitting an
///       Admin.ManageMasterData / Admin.ManageUsers write).
///   (b) a token carrying must_change_password=true gets 403 from a normal gated endpoint,
///       while /auth/change-password itself stays reachable (E1-08's RequirePasswordChangeFilter).
/// </summary>
public class PermissionDeniedTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public PermissionDeniedTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- N-2(a): missing-permission 403 ----------------------------------------

    [Fact]
    public async Task Associate_MissingAdminManageMasterData_Gets403OnMasterDataWrite()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/master-data/categories",
            new UpsertMasterDataRequest("Should Be Rejected", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Associate_MissingAdminManageUsers_Gets403OnAdminUsersEndpoint()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Associate_CanStillReadMasterData_PermissionScopingIsWriteOnly()
    {
        // The other direction of the same acceptance criterion: Associate must be able to
        // READ master data (any authenticated user can) even though it cannot WRITE it.
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- N-2(b): must-change-password 403, change-password stays reachable -----

    [Fact]
    public async Task MustChangePasswordToken_Gets403FromNormalGatedEndpoint_ButChangePasswordStaysReachable()
    {
        var email = $"mustchange-{Guid.NewGuid():N}@test.local";
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("Must Change", email, [_fixture.AssociateRoleId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateUserResult>())!;

        using var freshUserClient = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(freshUserClient, email, created.TemporaryPassword);
        login.MustChangePassword.Should().BeTrue("a newly created account always starts with MustChangePassword=true");
        freshUserClient.WithBearer(login.AccessToken);

        // A normal gated endpoint (master-data read requires only [Authorize], no specific
        // permission — the filter runs before any policy check) must reject with 403 while
        // the flag is set, per E1-08's RequirePasswordChangeFilter.
        var blockedResponse = await freshUserClient.GetAsync("/api/v1/master-data");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // change-password itself must remain reachable ([AllowMustChangePassword]).
        var changeResponse = await freshUserClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(created.TemporaryPassword, "Fresh-Changed-Pw1!"));
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // After changing, the SAME kind of endpoint must now succeed with the reissued token.
        var reissued = await changeResponse.Content.ReadFromJsonAsync<AuthResult>();
        freshUserClient.WithBearer(reissued!.AccessToken);
        var nowAllowedResponse = await freshUserClient.GetAsync("/api/v1/master-data");
        nowAllowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
