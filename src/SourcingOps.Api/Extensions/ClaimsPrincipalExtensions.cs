using System.Security.Claims;

namespace SourcingOps.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the "sub" claim (see feedback-jwt-claim-mapping memory: MapInboundClaims=false
    /// keeps short claim names, and Program.cs's TokenValidationParameters.NameClaimType is
    /// "sub", matching what JwtTokenGenerator writes) as the acting user's id. Every caller
    /// of this sits behind [Authorize]/a permission policy, where a missing or malformed
    /// "sub" indicates a token-integrity problem, not a normal application-level failure —
    /// it deliberately throws rather than returning a sentinel value.
    /// </summary>
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(value, out var userId))
        {
            throw new InvalidOperationException("Authenticated request is missing a valid 'sub' claim.");
        }

        return userId;
    }
}
