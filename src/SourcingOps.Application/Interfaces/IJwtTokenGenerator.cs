namespace SourcingOps.Application.Interfaces;

public sealed record TokenClaims(
    Guid UserId,
    string Email,
    string Name,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public sealed record GeneratedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Issues signed JWT access tokens. Implemented in Infrastructure. The implementation
/// MUST serialize `permissions` as a JSON array in the token payload even when there
/// are zero or one permissions — .NET's default claim handling collapses a single
/// repeated claim into a scalar, which breaks the frontend's array parsing.
/// </summary>
public interface IJwtTokenGenerator
{
    GeneratedToken GenerateAccessToken(TokenClaims claims);
}
