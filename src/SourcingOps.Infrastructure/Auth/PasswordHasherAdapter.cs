using Microsoft.AspNetCore.Identity;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Auth;

/// <summary>
/// Wraps <see cref="PasswordHasher{TUser}"/> (PBKDF2, framework-provided) standalone —
/// TECH_SPEC §4.2 explicitly avoids pulling in the rest of ASP.NET Core Identity.
/// </summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public PasswordVerifyResult Verify(User user, string hashedPassword, string providedPassword)
    {
        var result = _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerifyResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerifyResult.SuccessRehashNeeded,
            _ => PasswordVerifyResult.Failed
        };
    }
}
