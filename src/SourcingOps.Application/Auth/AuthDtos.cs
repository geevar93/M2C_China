namespace SourcingOps.Application.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserSummaryDto(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    bool MustChangePassword,
    UserSummaryDto User);

public enum ChangePasswordOutcome
{
    Success,
    IncorrectCurrentPassword,
    UserNotFound
}

public sealed record ChangePasswordResult(ChangePasswordOutcome Outcome, AuthResult? Result);
