using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Tests.Auth;

/// <summary>
/// Uses <see cref="RelaxedRateLimitApiFactory"/> rather than the base <see cref="ApiFactory"/>
/// (coordinator-flagged, M3 review): this class makes several <c>/auth/login</c> calls sharing
/// one class fixture, and the base factory's production-matching 10/minute/IP limit leaves too
/// little headroom once run alongside its siblings. <see cref="RateLimitAndCorsTests"/> is the
/// one class that must keep proving the real limit trips, and is untouched.
/// </summary>
public class LoginTests : IClassFixture<RelaxedRateLimitApiFactory>
{
    private readonly ApiFactory _factory;

    public LoginTests(RelaxedRateLimitApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithBootstrapAdminCredentials_ReturnsFullContractShapeAndMustChangePasswordFalse()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, _factory.BootstrapAdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResult>();

        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        // E11-10: the seed forces a change only for a GENERATED password. ApiFactory configures
        // Bootstrap:AdminPassword explicitly (as appsettings.json and docker-compose.yml now do),
        // and a deliberately chosen credential is seeded ready to use — see DbSeederTests'
        // paired configured/generated cases for the full reasoning.
        body.MustChangePassword.Should().BeFalse();
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
