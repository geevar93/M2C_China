using System.Security.Cryptography;

namespace SourcingOps.Application.Auth;

/// <summary>
/// Generates the opaque refresh token given to the client and the hash stored server-side
/// (TECH_SPEC §4.2: "random 256-bit value, stored hashed"). Pure BCL crypto — no
/// framework dependency.
/// </summary>
public static class RefreshTokenFactory
{
    public static string GeneratePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
        return Base64UrlEncode(bytes);
    }

    public static string Hash(string plainToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToHexString(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
