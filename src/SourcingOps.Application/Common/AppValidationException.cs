namespace SourcingOps.Application.Common;

/// <summary>
/// Thrown by Application services on a validation failure. The Api layer's global
/// exception handler (E1-06) maps this to an RFC 7807 `ProblemDetails` 400 response
/// carrying `Errors`, without leaking internals.
/// </summary>
public sealed class AppValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public AppValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [string.Empty] = [message] };
    }

    public AppValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public AppValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
