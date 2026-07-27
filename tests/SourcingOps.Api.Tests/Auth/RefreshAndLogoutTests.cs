using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.Auth;

/// <summary>
/// Uses <see cref="RelaxedRateLimitApiFactory"/> rather than the base <see cref="ApiFactory"/>
/// (coordinator-flagged, M3 review): this class makes several <c>/auth/login</c> calls sharing
/// one class fixture, and the base factory's production-matching 10/minute/IP limit leaves too
/// little headroom once run alongside its siblings. <see cref="RateLimitAndCorsTests"/> is the
/// one class that must keep proving the real limit trips, and is untouched. This class doesn't
/// mutate the bootstrap admin's password (unlike the old <c>ChangePasswordTests</c>), so sharing
/// one bootstrap-admin login across its methods is not itself a correctness risk — only the
/// rate-limit headroom needed fixing here.
/// </summary>
public class RefreshAndLogoutTests : IClassFixture<RelaxedRateLimitApiFactory>
{
    private readonly ApiFactory _factory;

    public RefreshAndLogoutTests(RelaxedRateLimitApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.BootstrapAdminEmail, _factory.BootstrapAdminPassword));
        // Coordinator-flagged fix: a bare EnsureSuccessStatusCode() throws with no status code
        // or response body, which is why the ChangePasswordTests failure took three runs to
        // diagnose. Every login helper in this test project now surfaces both on failure.
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewTokenPair()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await response.Content.ReadFromJsonAsync<AuthResult>();
        refreshed!.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_ReusingAnAlreadyRotatedToken_Returns401()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);
        await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken)); // rotates it

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401NotAServerError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("not-a-real-token-at-all"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Returns204_AndTheRefreshTokenStopsWorking()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);

        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(login.RefreshToken));
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshAttempt = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        refreshAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_CalledTwiceWithSameToken_IsIdempotent()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);

        var first = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(login.RefreshToken));
        var second = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(login.RefreshToken));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
