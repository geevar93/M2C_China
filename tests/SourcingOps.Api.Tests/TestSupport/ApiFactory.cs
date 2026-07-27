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
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("sourcingops_test")
        .WithUsername("app")
        .WithPassword("test-password")
        .Build();

    public string BootstrapAdminEmail { get; } = "bootstrap-admin@test.local";
    public string BootstrapAdminPassword { get; } = "Initial-Passw0rd!";
    public string JwtSigningKey { get; } = new('t', 40);

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
    }
}
