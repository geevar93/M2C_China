using Microsoft.AspNetCore.Mvc;
using SourcingOps.Application.Auth;

namespace SourcingOps.Api.Middleware;

/// <summary>
/// N-7 fix: rejects an already-issued access token whose `iat` predates the last recorded
/// revocation for that user (<see cref="ITokenRevocationService"/>), closing the
/// "deactivated user's bearer token still works for up to 8h" gap (ACTION_PLAN §10.2).
/// Runs after <c>UseAuthentication</c> — so <c>context.User</c> is already a validated,
/// non-expired, non-tampered token's claims — and before <c>UseAuthorization</c>/MVC, so a
/// revoked token never reaches a controller action or the password-change filter. One cache
/// lookup per authenticated request, never a DB read, per TECH_SPEC §4.3's explicit design.
/// Silently no-ops for unauthenticated requests (login/refresh/health) and for a token
/// missing/malforming `sub`/`iat` — that is either an anonymous endpoint or a token-integrity
/// problem JWT validation itself already would have rejected upstream.
/// </summary>
public sealed class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;

    public TokenRevocationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITokenRevocationService revocation)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var subClaim = context.User.FindFirst("sub")?.Value;
            var iatClaim = context.User.FindFirst("iat")?.Value;

            if (Guid.TryParse(subClaim, out var userId) && long.TryParse(iatClaim, out var iatUnixSeconds))
            {
                var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(iatUnixSeconds).UtcDateTime;

                if (await revocation.IsRevokedAsync(userId, issuedAtUtc, context.RequestAborted))
                {
                    await WriteRevokedProblemAsync(context);
                    return;
                }
            }
        }

        await _next(context);
    }

    private static Task WriteRevokedProblemAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Token revoked.",
            Detail = "This access token has been revoked. Please log in again.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            Instance = context.Request.Path
        };

        // See ExceptionHandlingMiddleware's identical note: WriteAsJsonAsync sets
        // Response.ContentType from its contentType parameter — must be passed explicitly
        // here or it silently overwrites the RFC 7807 media type.
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
