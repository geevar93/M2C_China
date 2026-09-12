using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SourcingOps.Api;
using SourcingOps.Api.Authorization;
using SourcingOps.Api.Filters;
using SourcingOps.Api.Middleware;
using SourcingOps.Application;
using SourcingOps.Domain.Constants;
using SourcingOps.Infrastructure;
using SourcingOps.Infrastructure.Auth;
using SourcingOps.Infrastructure.Persistence;
using SourcingOps.Infrastructure.Persistence.Seed;
using SourcingOps.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- Services -------------------------------------------------------------

// Ensures every ProblemDetails response (explicit Problem() calls, [ApiController]'s
// automatic validation failures, and the custom exception middleware below) is
// consistently served as application/problem+json (TECH_SPEC §4.8).
builder.Services.AddProblemDetails();

builder.Services.AddControllers(options =>
{
    // E1-08: enforced globally so no feature controller can forget it.
    options.Filters.Add<RequirePasswordChangeFilter>();
});
builder.Services.AddOpenApi();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var jwtOptions = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwtOptions);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // See feedback-jwt-claim-mapping: disable the legacy short->long claim-type
        // remap on the way in, and read claims back using the SAME short names used
        // when the token was issued (JwtTokenGenerator).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrEmpty(jwtOptions.SigningKey) ? new string('x', 32) : jwtOptions.SigningKey)),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// E3-01: real PermissionRequirement/PermissionAuthorizationHandler backing every
// [Authorize(Policy = "...")] attribute — MasterDataController/AdminUsersController are the
// first feature controllers to use them. One policy per catalog entry, so adding a
// permission is a one-line addition to PermissionCodes.All, not new plumbing.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, ChangeOwnPasswordAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var code in PermissionCodes.All)
    {
        options.AddPolicy(code, policy => policy.Requirements.Add(new PermissionRequirement(code)));
    }

    // Not one of the per-code policies above: /auth/change-password is satisfied by EITHER
    // Account.ChangeOwnPassword (voluntary, SuperAdmin-only by seeding) OR a token carrying
    // must_change_password=true (the forced remediation path every user must keep, or E11-01
    // accounts could never be activated). See ChangeOwnPasswordAuthorizationHandler.
    options.AddPolicy(ChangeOwnPasswordRequirement.PolicyName,
        policy => policy.Requirements.Add(new ChangeOwnPasswordRequirement()));
});

var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:4200";
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Configurable (default matches the original hardcoded 10/60s exactly — see
// appsettings.json's "RateLimiting" section) so integration tests that legitimately need
// many /auth/login calls across many test methods sharing one WebApplicationFactory (M2's
// AdminSeededFixture-based suites) can raise the limit via UseSetting without touching
// production behaviour. RateLimitAndCorsTests keeps proving the real default trips.
var loginPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:LoginPermitLimit") ?? 10;
var loginWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:LoginWindowSeconds") ?? 60;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // TECH_SPEC §4.8/§8: blunt brute-force attempts against /auth/login. Partitioned
    // per client IP so one abusive caller doesn't exhaust the budget for everyone.
    options.AddPolicy(RateLimiterPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(loginWindowSeconds),
                PermitLimit = loginPermitLimit,
                QueueLimit = 0
            }));

    // E9-10: the one anonymous document endpoint. Deliberately far more generous than login —
    // a legitimate recipient may tap the same link several times and a phone browser can issue
    // range requests — while still bounding what a scanner costs the box. Same per-IP fixed
    // window, same built-in middleware, no new dependency (C1).
    var sharedDocPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:SharedDocumentPermitLimit") ?? 60;
    var sharedDocWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:SharedDocumentWindowSeconds") ?? 60;

    options.AddPolicy(RateLimiterPolicies.SharedDocument, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(sharedDocWindowSeconds),
                PermitLimit = sharedDocPermitLimit,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ---- Fail-fast checks -------------------------------------------------------

if (app.Environment.IsProduction())
{
    var signingKeyBytes = Encoding.UTF8.GetByteCount(jwtOptions.SigningKey ?? string.Empty);
    if (signingKeyBytes < 32) // 256 bits, matches HMACSHA256's minimum
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey (env Jwt__SigningKey) is missing or shorter than 256 bits. " +
            "Refusing to start in Production with a weak or absent signing key.");
    }
}

// ---- Pipeline ----------------------------------------------------------------

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // TECH_SPEC §6: migrations auto-apply in dev only; production runs an explicit
    // `dotnet ef database update` deploy step (E2-07) and never auto-migrates.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}

// H-19: a fresh MinIO container starts with no buckets, so the first upload would fail with a
// NoSuchBucket that looks like a credentials problem. No-op unless Storage:Provider is S3.
await app.Services.EnsureFileStorageBucketAsync();

// Idempotent seeding (E1-03) runs every startup, dev and prod alike — by the time
// this runs in prod, migrations have already been applied by the explicit deploy step.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    await seeder.SeedAsync();
}

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();

// N-7: must run after authentication (needs context.User populated from a validated token)
// and before authorization/MVC (a revoked token must never reach a controller action).
app.UseMiddleware<TokenRevocationMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// Exposed (in the global namespace, alongside the compiler-generated top-level-statement
/// Program class it merges with) so WebApplicationFactory&lt;Program&gt; in the test project
/// can reference the real entry point.
/// </summary>
public partial class Program;
