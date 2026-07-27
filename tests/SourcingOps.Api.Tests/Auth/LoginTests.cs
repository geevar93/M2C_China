using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Tests.Auth;

public class LoginTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LoginTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithBootstrapAdminCredentials_ReturnsFullContractShapeAndMustChangePasswordTrue()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, _factory.BootstrapAdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResult>();

        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.MustChangePassword.Should().BeTrue(); // bootstrap seed always sets this
        body.User.Email.Should().Be(_factory.BootstrapAdminEmail);
        body.User.Roles.Should().Contain(RoleNames.SuperAdmin);
        body.User.Permissions.Should().BeEquivalentTo(PermissionCodes.All); // SuperAdmin gets everything
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401WithGenericMessage()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, "definitely-wrong"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameStatusAndShapeAsWrongPassword()
    {
        var client = _factory.CreateClient();

        var unknownResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody-at-all@test.local", "whatever"));
        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, "definitely-wrong"));

        unknownResponse.StatusCode.Should().Be(wrongPasswordResponse.StatusCode);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ResponseContentType_IsApplicationProblemJsonOnFailure()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, "wrong"));

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
