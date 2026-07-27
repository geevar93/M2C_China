using System.Net.Http.Headers;
using System.Net.Http.Json;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.TestSupport;

/// <summary>
/// Shared plumbing for M2 integration tests. Deliberately performs the bare minimum number
/// of <c>/auth/login</c> calls (rate-limited to 10/minute/IP — see RateLimiterPolicies.Login)
/// by provisioning a reusable admin + Associate token pair ONCE per test class via
/// <see cref="AdminSeededFixture"/>, rather than logging in fresh inside every test method.
/// </summary>
public static class AdminApiTestHelpers
{
    public static async Task<AuthResult> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Using an already-authenticated (password-changed) Super Admin client, creates a
    /// brand-new Associate user, logs in as them, and changes their password so the returned
    /// token is fully usable (MustChangePassword=false) — ready for "Associate can read, but
    /// cannot write master data / touch admin users" assertions.
    /// </summary>
    public static async Task<AuthResult> ProvisionActiveAssociateAsync(ApiFactory factory, HttpClient adminClient, Guid associateRoleId, string uniqueSuffix)
    {
        var email = $"associate-{uniqueSuffix}@test.local";
        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/users",
            new CreateUserRequest("Test Associate", email, [associateRoleId]));
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateUserResult>())!;

        using var newUserClient = factory.CreateClient();
        var firstLogin = await LoginAsync(newUserClient, email, created.TemporaryPassword);

        newUserClient.WithBearer(firstLogin.AccessToken);
        var changeResponse = await newUserClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(created.TemporaryPassword, "Associate-Changed-Pw1!"));
        changeResponse.EnsureSuccessStatusCode();
        return (await changeResponse.Content.ReadFromJsonAsync<AuthResult>())!;
    }
}
