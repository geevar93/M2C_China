using FluentAssertions;
using Moq;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Auth;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Auth;

public class AuthServiceTests
{
    private static AuthService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditLoggerMock)
    {
        auditLoggerMock = new Mock<IAuditLogger>();
        var jwtOptions = new JwtOptions { SigningKey = new string('k', 40), Issuer = "test-iss", Audience = "test-aud" };
        var authOptions = new AuthOptions { AccessTokenLifetimeMinutes = 480, RefreshTokenLifetimeDays = 30 };
        var tokenGenerator = new JwtTokenGenerator(jwtOptions, authOptions);

        return new AuthService(db, AuthTestData.RealPasswordHasher, tokenGenerator, auditLoggerMock.Object, authOptions);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokensAndUserSummary()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View", "Customers.Edit");
        var sut = CreateSut(db, out var auditLogger);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));

        result.Should().NotBeNull();
        result!.User.Id.Should().Be(user.Id);
        result.User.Roles.Should().ContainSingle().Which.Should().Be("Associate");
        result.User.Permissions.Should().BeEquivalentTo(["Customers.Edit", "Customers.View"]);
        result.MustChangePassword.Should().BeFalse();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var storedToken = db.RefreshTokens.Single();
        storedToken.TokenHash.Should().Be(RefreshTokenFactory.Hash(result.RefreshToken));

        auditLogger.Verify(a => a.LogAsync(user.Id, "LoginSucceeded", "User", user.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsNullAndGivesNoAccountExistenceHint()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var auditLogger);

        var result = await sut.LoginAsync(new LoginRequest("nobody@example.com", "whatever"));

        result.Should().BeNull();
        db.RefreshTokens.Should().BeEmpty();
        auditLogger.Verify(a => a.LogAsync(null, "LoginFailed", "User", null, It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out var auditLogger);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "totally-wrong"));

        result.Should().BeNull();
        auditLogger.Verify(a => a.LogAsync(user.Id, "LoginFailed", "User", user.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ReturnsNullEvenWithCorrectPassword()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        user.IsActive = false;
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_EmailComparisonIsCaseInsensitive()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);

        var result = await sut.LoginAsync(new LoginRequest(user.Email.ToUpperInvariant(), "Correct-Password1"));

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RotatesTokenAndReturnsNewPair()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);
        var login = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));

        var refreshed = await sut.RefreshAsync(new RefreshRequest(login!.RefreshToken));

        refreshed.Should().NotBeNull();
        refreshed!.RefreshToken.Should().NotBe(login.RefreshToken);

        var oldTokenHash = RefreshTokenFactory.Hash(login.RefreshToken);
        db.RefreshTokens.Single(t => t.TokenHash == oldTokenHash).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithUnknownToken_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.RefreshAsync(new RefreshRequest("not-a-real-token"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithAlreadyRevokedToken_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);
        var login = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));
        await sut.RefreshAsync(new RefreshRequest(login!.RefreshToken)); // revokes the original

        var reuse = await sut.RefreshAsync(new RefreshRequest(login.RefreshToken));

        reuse.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_RevokesToken_AndIsIdempotent()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);
        var login = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));

        await sut.LogoutAsync(new LogoutRequest(login!.RefreshToken));
        var tokenHash = RefreshTokenFactory.Hash(login.RefreshToken);
        db.RefreshTokens.Single(t => t.TokenHash == tokenHash).RevokedAt.Should().NotBeNull();

        // Calling logout again with the same (already-revoked) token must not throw.
        await sut.LogoutAsync(new LogoutRequest(login.RefreshToken));
    }

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_DoesNotThrow()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        await sut.LogoutAsync(new LogoutRequest("no-such-token"));
    }

    [Fact]
    public async Task ChangePasswordAsync_WithCorrectCurrentPassword_ClearsFlagAndIssuesFreshTokens()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        user.MustChangePassword = true;
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out var auditLogger);

        var result = await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Correct-Password1", "Brand-New-Password2"));

        result.Outcome.Should().Be(ChangePasswordOutcome.Success);
        result.Result!.MustChangePassword.Should().BeFalse();
        db.Users.Single(u => u.Id == user.Id).MustChangePassword.Should().BeFalse();
        auditLogger.Verify(a => a.LogAsync(user.Id, "PasswordChanged", "User", user.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_ReturnsIncorrectOutcomeAndDoesNotChangeAnything()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var originalHash = user.PasswordHash;
        var sut = CreateSut(db, out _);

        var result = await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("wrong-current", "Brand-New-Password2"));

        result.Outcome.Should().Be(ChangePasswordOutcome.IncorrectCurrentPassword);
        result.Result.Should().BeNull();
        db.Users.Single(u => u.Id == user.Id).PasswordHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithTooShortNewPassword_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);

        var act = async () => await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Correct-Password1", "short"));

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task ChangePasswordAsync_RevokesExistingRefreshTokens()
    {
        using var db = TestDbContextFactory.Create();
        var user = AuthTestData.CreateActiveUserWithRole(db, "Associate", "Correct-Password1", "Customers.View");
        var sut = CreateSut(db, out _);
        var login = await sut.LoginAsync(new LoginRequest(user.Email, "Correct-Password1"));

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Correct-Password1", "Brand-New-Password2"));

        var oldTokenHash = RefreshTokenFactory.Hash(login!.RefreshToken);
        db.RefreshTokens.Single(t => t.TokenHash == oldTokenHash).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithUnknownUserId_ReturnsUserNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordRequest("x", "Brand-New-Password2"));

        result.Outcome.Should().Be(ChangePasswordOutcome.UserNotFound);
    }
}
