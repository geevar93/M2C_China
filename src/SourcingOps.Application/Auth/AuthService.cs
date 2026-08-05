using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IAuditLogger _auditLogger;
    private readonly AuthOptions _options;
    private readonly ITokenRevocationService _tokenRevocation;

    // Fixed dummy hash used to burn comparable CPU time when the email is unknown, so an
    // unknown email and a wrong password are not distinguishable by response timing.
    // Produced once (Identity's PasswordHasher<T> ignores the TUser instance's data).
    private static readonly User DummyUser = new() { Id = Guid.Empty, Email = "no-such-user@invalid" };
    private string? _dummyHash;

    public AuthService(
        IAppDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IAuditLogger auditLogger,
        AuthOptions options,
        ITokenRevocationService tokenRevocation)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _auditLogger = auditLogger;
        _options = options;
        _tokenRevocation = tokenRevocation;
    }

    public async Task<AuthResult?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null)
        {
            // Burn comparable time verifying against a dummy hash so an unknown email
            // responds no faster than a wrong password for a real one.
            _dummyHash ??= _passwordHasher.Hash(DummyUser, Guid.NewGuid().ToString("N"));
            _passwordHasher.Verify(DummyUser, _dummyHash, request.Password);
            await _auditLogger.LogAsync(null, "LoginFailed", "User", null, new { request.Email, reason = "unknown-email" }, ct);
            return null;
        }

        var verifyResult = _passwordHasher.Verify(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerifyResult.Failed)
        {
            await _auditLogger.LogAsync(user.Id, "LoginFailed", "User", user.Id.ToString(), new { reason = "bad-password" }, ct);
            return null;
        }

        if (!user.IsActive)
        {
            // Same generic outcome as a bad password — do not confirm the account exists but is deactivated.
            await _auditLogger.LogAsync(user.Id, "LoginFailed", "User", user.Id.ToString(), new { reason = "inactive" }, ct);
            return null;
        }

        if (verifyResult == PasswordVerifyResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.Hash(user, request.Password);
        }

        var result = await IssueTokenPairAsync(user, ct);
        await _auditLogger.LogAsync(user.Id, "LoginSucceeded", "User", user.Id.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult?> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = RefreshTokenFactory.Hash(request.RefreshToken);

        var token = await _db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsActive || !token.User.IsActive)
        {
            if (token is not null)
            {
                await _auditLogger.LogAsync(token.UserId, "RefreshFailed", "RefreshToken", token.Id.ToString(), new { reason = "expired-or-revoked" }, ct);
            }
            return null;
        }

        token.RevokedAt = DateTime.UtcNow;
        var result = await IssueTokenPairAsync(token.User, ct);
        await _auditLogger.LogAsync(token.UserId, "RefreshSucceeded", "User", token.UserId.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var hash = RefreshTokenFactory.Hash(request.RefreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || token.RevokedAt is not null)
        {
            return; // idempotent — do not reveal whether the token existed
        }

        token.RevokedAt = DateTime.UtcNow;
        await _auditLogger.LogAsync(token.UserId, "Logout", "User", token.UserId.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.UserNotFound, null);
        }

        var verify = _passwordHasher.Verify(user, user.PasswordHash, request.CurrentPassword);
        if (verify == PasswordVerifyResult.Failed)
        {
            await _auditLogger.LogAsync(user.Id, "ChangePasswordFailed", "User", user.Id.ToString(), new { reason = "incorrect-current-password" }, ct);
            return new ChangePasswordResult(ChangePasswordOutcome.IncorrectCurrentPassword, null);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new AppValidationException("newPassword", "New password must be at least 8 characters long.");
        }

        // Defect fix, not an added strength rule (min-8 above is still the codebase's entire
        // password policy). Without this, a user under a forced change could "change" their
        // temporary password to itself: MustChangePassword would clear while the temp credential
        // that was written to a log or relayed out-of-band stays live — a real hole in E1-08. On
        // the voluntary path it also stops a pure no-op that nonetheless revokes every session.
        // Checked before any mutation, so a rejected call leaves the hash and refresh tokens
        // exactly as they were.
        if (string.Equals(request.NewPassword, request.CurrentPassword, StringComparison.Ordinal))
        {
            throw new AppValidationException("newPassword", "New password must be different from your current password.");
        }

        user.PasswordHash = _passwordHasher.Hash(user, request.NewPassword);
        user.MustChangePassword = false;

        // Revoke all existing refresh tokens — the response issues one fresh pair (defence in depth).
        var activeTokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
        }

        // E1-08 (task-1 follow-up): self-service change-password carries the same
        // "cut existing access now" intent as an admin-forced reset, but this path — unlike
        // AdminUserService.ResetPasswordAsync — reissues a token to the SAME caller in the
        // same request. That is the self-lockout hazard the coordinator flagged: if this were
        // ordered wrong, or if ITokenRevocationService.IsRevokedAsync didn't tolerate a token
        // minted in the same whole second as the revocation, the token handed back below
        // would be rejected by TokenRevocationMiddleware on the very next request. It is safe
        // by construction (see IsRevokedAsync's "strictly before" + floor-to-seconds
        // tolerance), proven by ChangePassword_ReturnedTokenIsUsableImmediately in
        // AuthEndpointsTests — not merely assumed here.
        await _tokenRevocation.RevokeAllIssuedBeforeNowAsync(user.Id, ct);

        var result = await IssueTokenPairAsync(user, ct);
        await _auditLogger.LogAsync(user.Id, "PasswordChanged", "User", user.Id.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);
        return new ChangePasswordResult(ChangePasswordOutcome.Success, result);
    }

    private async Task<AuthResult> IssueTokenPairAsync(User user, CancellationToken ct)
    {
        var roles = user.UserRoles.Select(ur => ur.Role).ToList();
        var permissions = PermissionResolver.Resolve(roles);
        var roleNames = PermissionResolver.RoleNames(roles);

        var accessToken = _tokenGenerator.GenerateAccessToken(new TokenClaims(
            user.Id, user.Email, user.Name, roleNames, permissions, user.MustChangePassword));

        var plainRefreshToken = RefreshTokenFactory.GeneratePlainToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = RefreshTokenFactory.Hash(plainRefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenLifetimeDays),
            CreatedAt = DateTime.UtcNow
        });

        await Task.CompletedTask;

        return new AuthResult(
            accessToken.AccessToken,
            plainRefreshToken,
            accessToken.ExpiresAtUtc,
            user.MustChangePassword,
            new UserSummaryDto(user.Id, user.Name, user.Email, roleNames, permissions));
    }
}
