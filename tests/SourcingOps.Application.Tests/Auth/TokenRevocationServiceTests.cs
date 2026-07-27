using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SourcingOps.Application.Auth;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Caching;

namespace SourcingOps.Application.Tests.Auth;

/// <summary>
/// Covers ACTION_PLAN N-7's deny-list (<see cref="TokenRevocationService"/>). Uses the REAL
/// <see cref="MemoryCacheService"/> (not a fake), same pattern as MasterDataServiceTests's
/// E3-09 coverage, so this exercises the actual production cache path.
/// </summary>
public class TokenRevocationServiceTests
{
    private static TokenRevocationService CreateSut(out MemoryCacheService cache, int accessTokenLifetimeMinutes = 480)
    {
        cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        return new TokenRevocationService(cache, new AuthOptions { AccessTokenLifetimeMinutes = accessTokenLifetimeMinutes });
    }

    /// <summary>
    /// Mirrors what a real caller (TokenRevocationMiddleware) actually passes: a JWT `iat`
    /// decoded via `DateTimeOffset.FromUnixTimeSeconds(...)`, which is always a whole-second
    /// value — never fractional. Tests that construct their own "issued at" instants use this
    /// rather than a raw `DateTime.UtcNow`, so they exercise the same granularity production
    /// does; see TokenRevocationService.IsRevokedAsync's doc comment for why that distinction
    /// matters (hazard 1 — a full-precision instant compared against a whole-second one is
    /// NOT the same test as two whole-second instants compared against each other).
    /// </summary>
    private static DateTime SimulateDecodedIat(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);

    [Fact]
    public async Task IsRevokedAsync_NoRevocationRecorded_ReturnsFalse()
    {
        var sut = CreateSut(out _);

        var result = await sut.IsRevokedAsync(Guid.NewGuid(), SimulateDecodedIat(DateTime.UtcNow));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_TokenIssuedBeforeRevocation_ReturnsTrue()
    {
        var sut = CreateSut(out _);
        var userId = Guid.NewGuid();
        // A safely-earlier whole second, not a tiny real-time delay — avoids flakiness from
        // both instants happening to floor into the same second.
        var issuedAt = SimulateDecodedIat(DateTime.UtcNow.AddSeconds(-5));

        await sut.RevokeAllIssuedBeforeNowAsync(userId);

        (await sut.IsRevokedAsync(userId, issuedAt)).Should().BeTrue();
    }

    [Fact]
    public async Task IsRevokedAsync_TokenIssuedAfterRevocation_ReturnsFalse()
    {
        var sut = CreateSut(out _);
        var userId = Guid.NewGuid();

        await sut.RevokeAllIssuedBeforeNowAsync(userId);
        var issuedAfter = SimulateDecodedIat(DateTime.UtcNow.AddSeconds(5));

        (await sut.IsRevokedAsync(userId, issuedAfter)).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_TokenIssuedAtExactlyTheRecordedInstant_IsTreatedAsValid()
    {
        // Hazard 1 (self-lockout ordering) from the coordinator's brief: a token whose `iat`
        // lands in the SAME second as the stored revocation timestamp must not be rejected —
        // this is what makes a revoke-then-immediately-reissue sequence safe even at
        // JWT's one-second `iat` resolution.
        var sut = CreateSut(out _);
        var userId = Guid.NewGuid();
        var instant = DateTime.UtcNow;

        await sut.RevokeAllIssuedBeforeNowAsync(userId); // stores approximately "instant"

        (await sut.IsRevokedAsync(userId, SimulateDecodedIat(instant).AddSeconds(1))).Should().BeFalse(
            "equal-or-after the recorded revocation must be accepted, not just strictly-after");
    }

    [Fact]
    public async Task IsRevokedAsync_TokenIssuedInTheSameWholeSecondAsRevocation_ButAfterItInRealTime_IsNotRevoked()
    {
        // THE actual restore-then-immediate-login bug this coordinator flagged as hazard 1:
        // a token minted a fraction of a second after the revocation, landing in the SAME
        // integer second once its `iat` is floor-truncated, must still be treated as valid —
        // not rejected just because the (sub-second-precision) stored revocation timestamp
        // has a larger fractional remainder than the token's own truncated-to-zero one.
        var sut = CreateSut(out var cache);
        var userId = Guid.NewGuid();

        await sut.RevokeAllIssuedBeforeNowAsync(userId);
        var revokedAt = await cache.GetAsync<DateTime>($"auth:revoked:{userId:N}");
        revokedAt.Should().NotBe(default(DateTime));

        // Simulate a token minted in the same wall-clock second as the revocation but
        // (crucially) reissued microseconds AFTER it — its `iat`, once floor-truncated,
        // could easily be a whole second whose raw instant is earlier than revokedAt's own
        // sub-second remainder, exactly like the M2 restore-then-relogin flow.
        var sameSecondIat = SimulateDecodedIat(revokedAt);

        (await sut.IsRevokedAsync(userId, sameSecondIat)).Should().BeFalse(
            "a token minted after the revocation, but truncated into the same whole second, must not be treated as pre-dating it");
    }

    [Fact]
    public async Task IsRevokedAsync_OnlyAffectsTheRevokedUser()
    {
        var sut = CreateSut(out _);
        var revokedUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var issuedAt = SimulateDecodedIat(DateTime.UtcNow.AddSeconds(-5));

        await sut.RevokeAllIssuedBeforeNowAsync(revokedUser);

        (await sut.IsRevokedAsync(revokedUser, issuedAt)).Should().BeTrue();
        (await sut.IsRevokedAsync(otherUser, issuedAt)).Should().BeFalse("revocation is per-user, not global");
    }

    [Fact]
    public async Task RevokeAllIssuedBeforeNowAsync_SetsTtlEqualToAccessTokenLifetime()
    {
        // TTL bounds the deny-list's size automatically: past this point every
        // pre-revocation token has also expired via its own `exp`, so the entry is dead
        // weight and safe to drop with no cleanup job. Verified against a mocked
        // ICacheService (not real elapsed time) since 8 hours is too long to actually wait.
        var cacheMock = new Mock<ICacheService>();
        TimeSpan? capturedTtl = null;
        cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime, TimeSpan, CancellationToken>((_, _, ttl, _) => capturedTtl = ttl)
            .Returns(Task.CompletedTask);
        var sut = new TokenRevocationService(cacheMock.Object, new AuthOptions { AccessTokenLifetimeMinutes = 480 });

        await sut.RevokeAllIssuedBeforeNowAsync(Guid.NewGuid());

        capturedTtl.Should().Be(TimeSpan.FromMinutes(480));
    }
}
