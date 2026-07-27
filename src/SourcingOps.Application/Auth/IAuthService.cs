namespace SourcingOps.Application.Auth;

public interface IAuthService
{
    /// <summary>Null return means invalid credentials or inactive account — caller must give no account-existence hint.</summary>
    Task<AuthResult?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Null return means the refresh token is missing, expired, or already revoked.</summary>
    Task<AuthResult?> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Idempotent — revokes the token if found; no error if it wasn't (avoids leaking token validity).</summary>
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);

    Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}
