using System.Net;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;

namespace SourcingOps.Api.Tests.Health;

public class HealthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Health_ReturnsHealthyWithRealDatabaseReachable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"database\":\"Healthy\"");
    }

    [Fact]
    public async Task Get_Health_DoesNotRequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
