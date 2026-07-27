using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Interfaces;

public enum PasswordVerifyResult
{
    Failed,
    Success,

    /// <summary>Password is correct but was hashed with an outdated algorithm/parameters and should be re-hashed.</summary>
    SuccessRehashNeeded
}

/// <summary>
/// Application-facing password hashing seam. Implemented in Infrastructure by wrapping
/// `Microsoft.AspNetCore.Identity.PasswordHasher&lt;User&gt;` directly — no dependency on
/// the rest of ASP.NET Core Identity (TECH_SPEC §4.2).
/// </summary>
public interface IPasswordHasher
{
    string Hash(User user, string password);
    PasswordVerifyResult Verify(User user, string hashedPassword, string providedPassword);
}
