using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.Auth;

/// <summary>
/// Root-fixed per coordinator diagnosis (M3 review): every test that changes a password now
/// provisions and acts on its OWN disposable account (via <see cref="AdminSeededFixture"/>'s
/// already-activated Super Admin client), instead of mutating the shared bootstrap admin's
/// credential in the test body with a bare "restore at the end" convention. That convention
/// was not actually order-independent under failure — an assertion failing between the mutate
/// and the restore left the bootstrap admin's password changed for every later test in the
/// class, turning one transient failure into a cascade of unrelated 401s from
/// <c>LoginAsync</c>. Provisioning a fresh account per test removes the shared mutable state
/// entirely, so there is nothing left to restore and nothing for one test to poison for
/// another. Also moved onto <see cref="AdminSeededFixture"/> (which already uses
/// <c>RelaxedRateLimitApiFactory</c>) rather than the base <c>ApiFactory</c>'s
/// production-matching 10/minute/IP limit — this class's ~7-8 logins under the old fixture
/// left too little headroom.
/// </summary>
public class ChangePasswordTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public ChangePasswordTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Creates a brand-new, nobody-else-touches-it account with MustChangePassword=true, via
    /// the fixture's Super Admin client. The caller drives its own login/change-password
    /// sequence against this account — nothing here is shared with any other test.
    /// </summary>
    private Task<(string Email, string TemporaryPassword)> ProvisionFreshUserAsync([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        AdminApiTestHelpers.CreateFreshUserAsync(_fixture.AdminClient, [_fixture.AssociateRoleId], $"ChangePw {testName}", Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task ChangePassword_WithoutAuthentication_Returns401()
    {
        var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("whatever", "Brand-New-Password2"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns400AndDoesNotChangeAnything()
    {
        var (email, temporaryPassword) = await ProvisionFreshUserAsync();
        var client = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        client.WithBearer(login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("wrong-current-password", "Brand-New-Password2"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Original (temporary) password must still work — nothing changed. Clear the stale
        // Bearer header before this raw anonymous call (see AdminApiTestHelpers.LoginAsync's
        // doc comment for why a leftover Authorization header on /auth/login is a real hazard,
        // not just untidy — reproduced live during the M3 review as the actual root cause of
        // an intermittent failure in this class).
        client.DefaultRequestHeaders.Authorization = null;
        var stillWorks = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, temporaryPassword));
        stillWorks.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithTooShortNewPassword_Returns400ProblemDetails()
    {
        var (email, temporaryPassword) = await ProvisionFreshUserAsync();
        var client = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        client.WithBearer(login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ChangePassword_UnderMustChangePasswordToken_IsAllowedThrough_AndClearsTheFlagOnSuccess()
    {
        // A freshly-created account's token has must_change_password=true — this endpoint is
        // exactly the one exemption RequirePasswordChangeFilter must let through (E1-08).
        var (email, temporaryPassword) = await ProvisionFreshUserAsync();
        var client = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        login.MustChangePassword.Should().BeTrue();
        client.WithBearer(login.AccessToken);

        const string newPassword = "Brand-New-Password2";
        var changeResponse = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, newPassword));

        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await changeResponse.Content.ReadFromJsonAsync<AuthResult>();
        changed!.MustChangePassword.Should().BeFalse();

        // Clear the stale Bearer header before this raw anonymous call — the client is still
        // carrying the OLD token, which change-password just revoked. Left attached, this
        // request would be intercepted by TokenRevocationMiddleware ("Token revoked", still a
        // 401, so the assertion below would coincidentally still pass) rather than genuinely
        // exercising "the old password is rejected by AuthController.Login" — see
        // AdminApiTestHelpers.LoginAsync's doc comment for the full root-cause explanation.
        client.DefaultRequestHeaders.Authorization = null;
        var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, temporaryPassword));
        oldPasswordLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newPasswordLogin = await AdminApiTestHelpers.LoginAsync(client, email, newPassword);
        newPasswordLogin.MustChangePassword.Should().BeFalse();

        // No restore needed — this test acted on its own disposable account, not a shared one.
    }

    [Fact]
    public async Task ChangePassword_ReturnedTokenIsUsableImmediately()
    {
        // Task-1 follow-up: change-password now feeds the SAME ITokenRevocationService
        // deny-list AdminUserService.Deactivate/AssignRoles already use (E1-08/N-7 risk
        // class), but this endpoint reissues a token to the same caller in the same call —
        // the self-lockout hazard the coordinator explicitly called out. This is the proof
        // through the REAL pipeline: TokenRevocationMiddleware runs on every authenticated
        // request, so if the revocation timestamp and the reissued token's `iat` were not
        // correctly ordered/tolerant, the very next request with the returned token would 401.
        var (email, temporaryPassword) = await ProvisionFreshUserAsync();
        var client = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        client.WithBearer(login.AccessToken);

        const string newPassword = "Immediate-Use-Pw3!";
        var changeResponse = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, newPassword));
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await changeResponse.Content.ReadFromJsonAsync<AuthResult>();

        // Use the token the change-password call JUST returned, on the very next request —
        // no delay, no re-login. Any authenticated endpoint proves it; master-data reads
        // require only [Authorize], not a specific permission.
        client.WithBearer(changed!.AccessToken);
        var immediateNextRequest = await client.GetAsync("/api/v1/master-data");
        immediateNextRequest.StatusCode.Should().Be(HttpStatusCode.OK,
            "the token change-password just issued must not be rejected by the revocation the same call recorded");

        // No restore needed — this test acted on its own disposable account, not a shared one.
    }

    // ---------------------------------------------------------------------------------------
    // E11-10 — the VOLUNTARY self-service path. Everything above exercises the forced flow, in
    // which the caller's must_change_password claim is what gets them through the policy. The
    // three tests below are the ones that actually pin the new behaviour: a Super Admin with
    // nothing forced on them may still change their own password, and an ordinary activated
    // user may not. Each provisions its own disposable account for the same reason the class
    // summary gives — never mutate the shared fixture's credentials.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ChangePassword_VoluntarilyBySuperAdminWithNothingForced_Returns200_AndTheNewPasswordWorks()
    {
        // A SECOND Super Admin, not the fixture's — its first (forced) change activates it, so
        // by the time we act it carries must_change_password=false and reaches the endpoint
        // purely on Account.ChangeOwnPassword.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var activated = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, [_fixture.SuperAdminRoleId], "Voluntary Admin", suffix, "Activated-Pw1!");
        activated.MustChangePassword.Should().BeFalse("this account is already activated — nothing is being forced");
        activated.User.Permissions.Should().Contain("Account.ChangeOwnPassword");

        var client = _fixture.Factory.CreateClient().WithBearer(activated.AccessToken);

        const string newPassword = "Voluntary-Pw2!";
        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("Activated-Pw1!", newPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await response.Content.ReadFromJsonAsync<AuthResult>();
        changed!.MustChangePassword.Should().BeFalse("a voluntary change must not push the caller into the forced flow");

        client.DefaultRequestHeaders.Authorization = null;
        var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(activated.User.Email, "Activated-Pw1!"));
        oldPasswordLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newPasswordLogin = await AdminApiTestHelpers.LoginAsync(client, activated.User.Email, newPassword);
        newPasswordLogin.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_VoluntarilyByActivatedNonAdmin_Returns403()
    {
        // The whole point of E11-10's restriction. This Associate is fully activated, so it has
        // NEITHER lever: no must_change_password claim and no Account.ChangeOwnPassword (that
        // code is in PermissionCodes.AdminOnly, which the seeder withholds from Associate).
        // Note this is a 403 from the policy, not a 400 from validation — the request body is
        // entirely valid and must still be refused.
        var client = _fixture.Factory.CreateClient().WithBearer(_fixture.AssociateAuth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("Associate-Changed-Pw1!", "Associate-Sneaky-Pw2!"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And the refusal was real: the Associate's existing password still works.
        client.DefaultRequestHeaders.Authorization = null;
        var stillWorks = await AdminApiTestHelpers.LoginAsync(client, _fixture.AssociateAuth.User.Email, "Associate-Changed-Pw1!");
        stillWorks.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChangePassword_ReusingTheCurrentPassword_Returns400_AndDoesNotClearTheForcedFlag()
    {
        var (email, temporaryPassword) = await ProvisionFreshUserAsync();
        var client = _fixture.Factory.CreateClient();
        var login = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        login.MustChangePassword.Should().BeTrue();
        client.WithBearer(login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, temporaryPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // The flag must still be set — otherwise a user could "activate" an account while the
        // temporary password that was handed out stays live.
        client.DefaultRequestHeaders.Authorization = null;
        var reLogin = await AdminApiTestHelpers.LoginAsync(client, email, temporaryPassword);
        reLogin.MustChangePassword.Should().BeTrue();
    }
}
