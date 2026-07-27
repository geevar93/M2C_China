using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Tests.Auth;

public class RateLimitAndCorsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RateLimitAndCorsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_RepeatedRapidly_EventuallyReturns429TooManyRequests()
    {
        // Program.cs configures a 10-requests-per-minute fixed window on /auth/login
        // (TECH_SPEC §4.8/§8) to blunt brute force. Deliberately wrong credentials —
        // the limiter must trip regardless of whether the login itself would succeed.
        var client = _factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 15; i++)
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "wrong"));
            if (last.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_FromConfiguredFrontendOrigin_ReceivesCorsAllowOriginHeader()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody@test.local", "wrong"))
        };
        request.Headers.Add("Origin", "http://localhost:4200"); // matches Cors:FrontendOrigin in ApiFactory

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values!.Should().Contain("http://localhost:4200");
    }

    /// <summary>
    /// The permit limit moved from a hardcoded 10 to configuration-driven (M2), so it needs
    /// its own proof beyond "the unchanged default of 10 still trips" (the test above): that
    /// a DIFFERENT configured value is genuinely enforced, not silently ignored in favour of
    /// some other hardcoded number. Uses its own tiny-limit factory instance rather than the
    /// shared class fixture, since this needs a limit no other test in this class shares.
    /// </summary>
    [Fact]
    public async Task Login_WithADifferentConfiguredLimit_TripsAtExactlyThatCount()
    {
        // Deliberately NOT `await using`: IAsyncDisposable.DisposeAsync() on
        // WebApplicationFactory only tears down the test host — the Postgres container is
        // stopped by ApiFactory's explicit IAsyncLifetime.DisposeAsync() override, which
        // `await using` would bypass and leak a running container.
        var tinyLimitFactory = new TinyRateLimitApiFactory();
        try
        {
            await tinyLimitFactory.InitializeAsync();
            var client = tinyLimitFactory.CreateClient();

            var first = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "wrong"));
            var second = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "wrong"));
            var third = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "wrong"));

            first.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
            second.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
            third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "LoginRateLimitPermitLimit=2 must be genuinely enforced, not just the production default of 10");
        }
        finally
        {
            await ((IAsyncLifetime)tinyLimitFactory).DisposeAsync();
        }
    }

    private sealed class TinyRateLimitApiFactory : ApiFactory
    {
        protected override int LoginRateLimitPermitLimit => 2;
    }
}
