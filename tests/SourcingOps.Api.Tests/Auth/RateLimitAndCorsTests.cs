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
}
