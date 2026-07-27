using System.Net.Http.Json;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Auth;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Tests.TestSupport;

/// <summary>
/// One-time-per-test-class setup shared via <c>IClassFixture</c>: a running
/// <see cref="ApiFactory"/>, a Super Admin client with its bootstrap password already
/// changed (the seed always sets <c>MustChangePassword=true</c> — see DbSeeder), and one
/// fully-activated Associate client. Built once so M2's integration tests do not each spend
/// their own <c>/auth/login</c> call against the 10/minute/IP rate limit (RateLimiterPolicies.Login).
/// </summary>
public sealed class AdminSeededFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new RelaxedRateLimitApiFactory();

    public HttpClient AdminClient { get; private set; } = null!;
    public AuthResult AdminAuth { get; private set; } = null!;

    public HttpClient AssociateClient { get; private set; } = null!;
    public AuthResult AssociateAuth { get; private set; } = null!;

    public Guid AssociateRoleId { get; private set; }
    public Guid SuperAdminRoleId { get; private set; }

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)Factory).InitializeAsync();

        AdminClient = Factory.CreateClient();
        var bootstrapLogin = await AdminApiTestHelpers.LoginAsync(AdminClient, Factory.BootstrapAdminEmail, Factory.BootstrapAdminPassword);
        AdminClient.WithBearer(bootstrapLogin.AccessToken);

        var changeResponse = await AdminClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(Factory.BootstrapAdminPassword, "Bootstrap-Changed-Pw1!"));
        changeResponse.EnsureSuccessStatusCode();
        AdminAuth = (await changeResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        AdminClient.WithBearer(AdminAuth.AccessToken);

        var rolesResponse = await AdminClient.GetAsync("/api/v1/admin/roles");
        rolesResponse.EnsureSuccessStatusCode();
        var roles = (await rolesResponse.Content.ReadFromJsonAsync<List<RoleDto>>())!;
        AssociateRoleId = roles.Single(r => r.Name == RoleNames.Associate).Id;
        SuperAdminRoleId = roles.Single(r => r.Name == RoleNames.SuperAdmin).Id;

        AssociateAuth = await AdminApiTestHelpers.ProvisionActiveAssociateAsync(Factory, AdminClient, AssociateRoleId, Guid.NewGuid().ToString("N")[..8]);
        AssociateClient = Factory.CreateClient().WithBearer(AssociateAuth.AccessToken);
    }

    public async Task DisposeAsync()
    {
        AdminClient.Dispose();
        AssociateClient.Dispose();
        await ((IAsyncLifetime)Factory).DisposeAsync();
    }
}
