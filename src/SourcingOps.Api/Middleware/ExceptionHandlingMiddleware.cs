using Microsoft.AspNetCore.Mvc;
using SourcingOps.Application.Common;

namespace SourcingOps.Api.Middleware;

/// <summary>
/// Global exception handler (TECH_SPEC §4.8, §8): unhandled errors become RFC 7807
/// `ProblemDetails`. Never leaks a stack trace or an internal identifier — the full
/// exception is logged server-side with a correlation id that IS safe to return.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppValidationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed.", ex.Errors, null);
        }
        catch (UnauthorizedAccessException)
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden.", null, null);
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null, correlationId);
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        IReadOnlyDictionary<string, string[]>? errors,
        string? correlationId)
    {
        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://tools.ietf.org/html/rfc9110#section-{(statusCode == 400 ? "15.5.1" : statusCode == 403 ? "15.5.4" : "15.6.1")}",
            Instance = context.Request.Path
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        if (correlationId is not null)
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        // WriteAsJsonAsync sets Response.ContentType itself from its contentType
        // parameter (defaulting to "application/json" if omitted) — it must be passed
        // explicitly here, or it silently overwrites the RFC 7807 media type.
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
