using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.Auth;

public class ChangePasswordTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ChangePasswordTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> LoginAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(_factory.BootstrapAdminEmail, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task ChangePassword_WithoutAuthentication_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("whatever", "Brand-New-Password2"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns400AndDoesNotChangeAnything()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, _factory.BootstrapAdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("wrong-current-password", "Brand-New-Password2"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Original password must still work — nothing changed.
        var stillWorks = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, _factory.BootstrapAdminPassword));
        stillWorks.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithTooShortNewPassword_Returns400ProblemDetails()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, _factory.BootstrapAdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(_factory.BootstrapAdminPassword, "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ChangePassword_UnderMustChangePasswordToken_IsAllowedThrough_AndClearsTheFlagOnSuccess()
    {
        // The bootstrap admin's token has must_change_password=true — this endpoint is
        // exactly the one exemption RequirePasswordChangeFilter must let through
        // (E1-08). Restores the original password at the end so this test class stays
        // order-independent regardless of how the other tests in it are sequenced.
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, _factory.BootstrapAdminPassword);
        login.MustChangePassword.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        const string newPassword = "Brand-New-Password2";
        var changeResponse = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(_factory.BootstrapAdminPassword, newPassword));

        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await changeResponse.Content.ReadFromJsonAsync<AuthResult>();
        changed!.MustChangePassword.Should().BeFalse();

        var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, _factory.BootstrapAdminPassword));
        oldPasswordLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newPasswordLogin = await LoginAsync(client, newPassword);
        newPasswordLogin.MustChangePassword.Should().BeFalse();

        // Restore original state for order-independence.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newPasswordLogin.AccessToken);
        var revert = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(newPassword, _factory.BootstrapAdminPassword));
        revert.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
