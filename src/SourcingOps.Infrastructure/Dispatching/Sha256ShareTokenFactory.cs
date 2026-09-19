using System.Security.Cryptography;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Dispatching;

/// <summary>
/// E9-10's token primitive. 32 bytes (256 bits) from <see cref="RandomNumberGenerator"/>,
/// hex-encoded to 64 lowercase characters. Hex rather than Base64Url on purpose: Base64Url's
/// '_' and '-' collide with WhatsApp's message formatting ('_italic_') and its link detection
/// (a trailing '-'/'_' is dropped), which left the shared link unclickable in the chat.
/// [0-9a-f] survives being pasted into a message and re-parsed out of a URL path untouched.
///
/// 256 bits is not arbitrary: this token is a bearer capability with no second factor, so its
/// only defence against enumeration is search-space size. At 2^256 the space is far beyond
/// brute force even without the rate limiter the endpoint also carries.
///
/// <see cref="Hash"/> is a bare SHA-256, deliberately — see <see cref="IShareTokenFactory.Hash"/>
/// and <c>DocumentShareLink.TokenHash</c>. PBKDF2 (what <c>PasswordHasherAdapter</c> uses for
/// passwords) would be wrong here on both counts: it is salted, which breaks lookup-by-hash, and
/// its work factor exists to slow dictionary attacks against low-entropy human input, which a
/// 256-bit random token is not.
/// </summary>
public sealed class Sha256ShareTokenFactory : IShareTokenFactory
{
    private const int TokenBytes = 32;

    public string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToHexStringLower(bytes);
    }

    public string Hash(string token)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexStringLower(digest);
    }
}
