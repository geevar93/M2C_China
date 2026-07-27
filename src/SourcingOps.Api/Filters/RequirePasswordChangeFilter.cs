using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SourcingOps.Api.Filters;

/// <summary>
/// TECH_SPEC §4.2 / E1-08: while a token carries `must_change_password=true`, every
/// endpoint except change-password returns 403. `[AllowAnonymous]` endpoints (login,
/// refresh, logout, health) are skipped entirely — they were never gated by this in
/// the first place. Registered globally in Program.cs.
/// </summary>
public sealed class RequirePasswordChangeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;

        var allowAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var allowMustChangePassword = metadata.OfType<AllowMustChangePasswordAttribute>().Any();

        if (allowAnonymous || allowMustChangePassword)
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var mustChangePassword = user.FindFirst("must_change_password")?.Value;
        if (string.Equals(mustChangePassword, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Password change required.",
                Detail = "This account must change its password before using any other endpoint.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
