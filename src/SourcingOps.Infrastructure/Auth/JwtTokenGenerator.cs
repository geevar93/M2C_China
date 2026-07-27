using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Auth;

/// <summary>
/// Issues JWT access tokens (TECH_SPEC §4.2). Uses the newer <see cref="JsonWebTokenHandler"/>
/// rather than the legacy <c>JwtSecurityTokenHandler</c> so we can put `permissions` in the
/// `SecurityTokenDescriptor.Claims` dictionary as an explicit array value — this is what
/// forces it to serialize as a JSON array even with zero or one entries. Adding the same
/// values through repeated <see cref="Claim"/> objects (the natural approach for `role`)
/// collapses to a scalar when there is exactly one, which is exactly the frontend-breaking
/// behaviour the spec calls out.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;
    private readonly AuthOptions _authOptions;

    public JwtTokenGenerator(JwtOptions options, AuthOptions authOptions)
    {
        _options = options;
        _authOptions = authOptions;
    }

    public GeneratedToken GenerateAccessToken(TokenClaims claims)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_authOptions.AccessTokenLifetimeMinutes);

        // "role" — one Claim per role, per TECH_SPEC §4.2's literal wording. With exactly
        // one role (the Phase-1 norm) this naturally serializes as a scalar; with two or
        // more it naturally becomes an array. Not forced, unlike "permissions" below,
        // because the spec only flags the array requirement for permissions.
        var roleClaims = claims.Roles.Select(r => new Claim("role", r));
        var subject = new ClaimsIdentity(roleClaims);

        var payloadClaims = new Dictionary<string, object>
        {
            ["sub"] = claims.UserId.ToString(),
            ["email"] = claims.Email,
            ["name"] = claims.Name,
            ["must_change_password"] = claims.MustChangePassword ? "true" : "false",
            // Explicit array value in the Claims dictionary — always serializes as a JSON
            // array, regardless of Permissions.Count (0, 1, or many). Do not change this to
            // a ClaimsIdentity-based repeated-Claim approach; see the class doc comment.
            ["permissions"] = claims.Permissions.ToArray()
        };

        var signingKeyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(signingKeyBytes), SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Claims = payloadClaims,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = DateTime.UtcNow,
            Expires = expiresAtUtc,
            SigningCredentials = credentials
        };

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new GeneratedToken(token, expiresAtUtc);
    }
}
