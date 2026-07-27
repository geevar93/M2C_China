using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.TestSupport;

/// <summary>
/// Shared plumbing for M2+ integration tests. Deliberately performs the bare minimum number
/// of <c>/auth/login</c> calls (rate-limited to 10/minute/IP — see RateLimiterPolicies.Login)
/// by provisioning a reusable admin + Associate token pair ONCE per test class via
/// <see cref="AdminSeededFixture"/>, rather than logging in fresh inside every test method.
/// </summary>
public static class AdminApiTestHelpers
{
    /// <summary>
    /// Coordinator-diagnosed fix: a bare <c>EnsureSuccessStatusCode()</c> throws with no status
    /// code or response body, which is exactly why an intermittent auth-test failure took two
    /// people and three runs to diagnose. Every "assert this call succeeded" check in this test
    /// project should go through this instead, so a failure is self-diagnosing from one run.
    /// </summary>
    public static async Task EnsureSuccessOrThrowWithBodyAsync(this HttpResponseMessage response,
        [CallerArgumentExpression(nameof(response))] string? expression = null)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"Expected a success status code for '{expression}' but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    public static async Task<AuthResult> LoginAsync(HttpClient client, string email, string password)
    {
        // Root-cause fix (coordinator review, M3): /auth/login is [AllowAnonymous], but JWT
        // bearer AUTHENTICATION still runs for any request carrying an Authorization header —
        // AllowAnonymous only skips the AUTHORIZATION check. If the caller's HttpClient still
        // has a Bearer token attached from an earlier step in the same test (DefaultRequestHeaders
        // persist across requests on one HttpClient instance) and that token has since been
        // revoked (e.g. by a password change earlier in the same test), TokenRevocationMiddleware
        // intercepts THIS login request before it ever reaches AuthController.Login and returns
        // 401 "Token revoked" — a confusing, genuinely timing-dependent failure (it only
        // manifests when the login and the revoking action land in different wall-clock
        // seconds) that looks like a login bug but has nothing to do with login itself. This is
        // the actual root cause behind the intermittent ChangePasswordTests failure the
        // coordinator reported (reproduced directly: see the exception message this now
        // surfaces). Clearing the header here makes this helper's contract genuinely "log in",
        // independent of whatever the client was doing beforehand.
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Using an already-authenticated (password-changed) Super Admin client, creates a
    /// brand-new user account and returns it as (email, temporaryPassword) WITHOUT logging in
    /// or changing its password — the caller drives that sequence itself. This is the shape a
    /// test needs when it specifically wants to exercise the fresh-account/MustChangePassword=true
    /// state (e.g. change-password tests) on an account nobody else touches, rather than a
    /// ready-to-use activated client (see <see cref="ProvisionActiveUserAsync"/> below for that).
    /// </summary>
    public static async Task<(string Email, string TemporaryPassword)> CreateFreshUserAsync(
        HttpClient adminClient, IReadOnlyList<Guid>? roleIds, string name, string uniqueSuffix)
    {
        var email = $"{name.ToLowerInvariant().Replace(' ', '-')}-{uniqueSuffix}@test.local";
        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest(name, email, roleIds));
        await createResponse.EnsureSuccessOrThrowWithBodyAsync();
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateUserResult>())!;
        return (email, created.TemporaryPassword);
    }

    /// <summary>
    /// Using an already-authenticated (password-changed) Super Admin client, creates a
    /// brand-new Associate user, logs in as them, and changes their password so the returned
    /// token is fully usable (MustChangePassword=false) — ready for "Associate can read, but
    /// cannot write master data / touch admin users" assertions.
    /// </summary>
    public static Task<AuthResult> ProvisionActiveAssociateAsync(ApiFactory factory, HttpClient adminClient, Guid associateRoleId, string uniqueSuffix) =>
        ProvisionActiveUserAsync(factory, adminClient, [associateRoleId], "Test Associate", uniqueSuffix, "Associate-Changed-Pw1!");

    /// <summary>
    /// General form of <see cref="ProvisionActiveAssociateAsync"/> — <paramref name="roleIds"/>
    /// may be empty, which is exactly what a DoD permission-denied ("caller lacking the
    /// permission gets 403") test needs: a fully activated account that legitimately carries
    /// an EMPTY permission set, as opposed to Associate (which holds every non-Admin.* permission,
    /// including Customers.View/.Edit, and so cannot exercise a Customers.* 403 path).
    /// </summary>
    public static async Task<AuthResult> ProvisionActiveUserAsync(ApiFactory factory, HttpClient adminClient, IReadOnlyList<Guid>? roleIds, string name, string uniqueSuffix, string newPassword)
    {
        var (email, temporaryPassword) = await CreateFreshUserAsync(adminClient, roleIds, name, uniqueSuffix);

        using var newUserClient = factory.CreateClient();
        var firstLogin = await LoginAsync(newUserClient, email, temporaryPassword);

        newUserClient.WithBearer(firstLogin.AccessToken);
        var changeResponse = await newUserClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, newPassword));
        await changeResponse.EnsureSuccessOrThrowWithBodyAsync();
        return (await changeResponse.Content.ReadFromJsonAsync<AuthResult>())!;
    }
}
