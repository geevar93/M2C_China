using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace SourcingOps.Api.Tests.TestSupport;

/// <summary>
/// TECH_SPEC §4.1: WebApplicationFactory-based integration tests against a Testcontainers
/// Postgres instance. Runs real migrations and real startup seeding against a disposable
/// container per test class — this is the "real" auth/DB path, not a mock.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The image moved from .WithImage() into the constructor when Testcontainers went to 4.15
    // (bumped to match Testcontainers.Minio, which the H-19 S3 tests need); the parameterless
    // builder is obsolete. Same image as before — postgres:16-alpine, matching docker-compose.yml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sourcingops_test")
        .WithUsername("app")
        .WithPassword("test-password")
        .Build();

    public string BootstrapAdminEmail { get; } = "bootstrap-admin@test.local";
    public string BootstrapAdminPassword { get; } = "Initial-Passw0rd!";
    public string JwtSigningKey { get; } = new('t', 40);

    /// <summary>
    /// Matches the production default exactly (appsettings.json's "RateLimiting" section) —
    /// overridden ONLY by <see cref="RelaxedRateLimitApiFactory"/>, which M2's
    /// AdminSeededFixture-based suites use because they legitimately call /auth/login many
    /// times across many test methods sharing one WebApplicationFactory. Auth/RateLimitAndCorsTests
    /// keeps using the base (unmodified) value so it keeps proving the real limit trips.
    /// </summary>
    protected virtual int LoginRateLimitPermitLimit => 10;
    protected virtual int LoginRateLimitWindowSeconds => 60;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync(); // shuts down the WebApplicationFactory's own test host
        await _postgres.StopAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // enables auto-migrate-on-startup (TECH_SPEC §6)

        // NOTE: Program.cs reads several config values (connection string, JWT signing
        // key, bootstrap admin) synchronously while building services, i.e. before
        // WebApplicationBuilder.Build() returns. WebApplicationBuilder's Configuration is
        // a ConfigurationManager that is materialized eagerly at CreateBuilder() time, so
        // a ConfigureAppConfiguration(...).AddInMemoryCollection(...) callback here runs
        // too late — it's appended as a config source after Program.cs already read (and
        // captured into local variables) the un-overridden appsettings.json values. This
        // was diagnosed by an [DIAG] Console.WriteLine that showed appsettings.json's
        // placeholder connection string being used despite the override below.
        // UseSetting(...) is the documented fix: it seeds the host's base configuration
        // BEFORE Program.cs runs, so it is visible to code that reads configuration
        // synchronously during service registration.
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("Jwt:Issuer", "SourcingOps.Tests");
        builder.UseSetting("Jwt:Audience", "SourcingOps.Tests.Client");
        builder.UseSetting("Bootstrap:AdminEmail", BootstrapAdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", BootstrapAdminPassword);
        builder.UseSetting("Cors:FrontendOrigin", "http://localhost:4200");
        builder.UseSetting("Caching:Provider", "InMemory");
        builder.UseSetting("RateLimiting:LoginPermitLimit", LoginRateLimitPermitLimit.ToString());
        builder.UseSetting("RateLimiting:LoginWindowSeconds", LoginRateLimitWindowSeconds.ToString());
    }
}

/// <summary>
/// Used only by M2's AdminSeededFixture (MasterData/AdminUsers/Authorization integration
/// test classes) — those provision several accounts and log in repeatedly across many test
/// methods sharing one class fixture, which would otherwise trip the same 10/minute/IP limit
/// Auth/RateLimitAndCorsTests exists to prove is real. A very high limit keeps those suites
/// deterministic without touching the production-matching default every other ApiFactory
/// consumer still gets.
/// </summary>
public sealed class RelaxedRateLimitApiFactory : ApiFactory
{
    protected override int LoginRateLimitPermitLimit => 100_000;
}
