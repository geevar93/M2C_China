using System.Text.Json;
using FluentAssertions;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Auth;

namespace SourcingOps.Application.Tests.Auth;

/// <summary>
/// Covers the auth contract's explicitly-called-out critical detail: `permissions` MUST
/// serialize as a JSON array in the token payload even with zero or one entries. .NET's
/// default claim handling collapses a single repeated claim into a scalar, which breaks
/// the frontend's array parsing — see JwtTokenGenerator's doc comment for the fix.
/// </summary>
public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateSut() =>
        new(new JwtOptions { SigningKey = new string('k', 40), Issuer = "test-iss", Audience = "test-aud" },
            new AuthOptions { AccessTokenLifetimeMinutes = 480 });

    private static JsonElement GetPayloadClaim(string jwt, string claimName)
    {
        var payloadSegment = jwt.Split('.')[1];
        var padded = payloadSegment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(claimName).Clone();
    }

    [Fact]
    public void GenerateAccessToken_WithExactlyOnePermission_SerializesPermissionsAsJsonArray()
    {
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["Associate"], ["Customers.View"], false);

        var token = sut.GenerateAccessToken(claims);
        var permissionsElement = GetPayloadClaim(token.AccessToken, "permissions");

        permissionsElement.ValueKind.Should().Be(JsonValueKind.Array);
        permissionsElement.GetArrayLength().Should().Be(1);
        permissionsElement[0].GetString().Should().Be("Customers.View");
    }

    [Fact]
    public void GenerateAccessToken_WithZeroPermissions_SerializesPermissionsAsEmptyJsonArray()
    {
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["Associate"], [], false);

        var token = sut.GenerateAccessToken(claims);
        var permissionsElement = GetPayloadClaim(token.AccessToken, "permissions");

        permissionsElement.ValueKind.Should().Be(JsonValueKind.Array);
        permissionsElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void GenerateAccessToken_WithManyPermissions_SerializesPermissionsAsJsonArray()
    {
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["SuperAdmin"], ["A.View", "A.Edit", "B.View"], false);

        var token = sut.GenerateAccessToken(claims);
        var permissionsElement = GetPayloadClaim(token.AccessToken, "permissions");

        permissionsElement.ValueKind.Should().Be(JsonValueKind.Array);
        permissionsElement.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void GenerateAccessToken_SetsMustChangePasswordClaimAsLowercaseStringBoolean()
    {
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["Associate"], [], true);

        var token = sut.GenerateAccessToken(claims);
        var element = GetPayloadClaim(token.AccessToken, "must_change_password");

        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("true");
    }

    [Fact]
    public void GenerateAccessToken_WithSingleRole_DoesNotForceArray_MatchesLiteralSpecWording()
    {
        // Documents the deliberate asymmetry with `permissions`: the spec only calls out
        // the array-forcing requirement for permissions. A single role naturally
        // serializes as a scalar via the repeated-Claim mechanism (ClaimsIdentity), which
        // is what "one claim per role" produces when there's exactly one.
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["Associate"], [], false);

        var token = sut.GenerateAccessToken(claims);
        var element = GetPayloadClaim(token.AccessToken, "role");

        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("Associate");
    }

    [Fact]
    public void GenerateAccessToken_WithMultipleRoles_SerializesRoleAsJsonArray()
    {
        var sut = CreateSut();
        var claims = new TokenClaims(Guid.NewGuid(), "a@b.com", "A B", ["Associate", "SuperAdmin"], [], false);

        var token = sut.GenerateAccessToken(claims);
        var element = GetPayloadClaim(token.AccessToken, "role");

        element.ValueKind.Should().Be(JsonValueKind.Array);
        element.GetArrayLength().Should().Be(2);
    }
}
