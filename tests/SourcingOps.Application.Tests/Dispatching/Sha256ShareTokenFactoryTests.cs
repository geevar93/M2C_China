using FluentAssertions;
using SourcingOps.Infrastructure.Dispatching;

namespace SourcingOps.Application.Tests.Dispatching;

/// <summary>
/// E9-10's token primitive, tested by property rather than by value — a test that pinned a
/// specific token would be asserting that the RNG is broken. Sits alongside
/// <see cref="WhatsAppDeepLinkSenderTests"/> for the same reason that suite does: it exercises
/// an Infrastructure type in isolation from the service that consumes it.
/// </summary>
public class Sha256ShareTokenFactoryTests
{
    private readonly Sha256ShareTokenFactory _sut = new();

    /// <summary>
    /// 32 random bytes hex-encode to 64 characters. The length matters because it
    /// is the visible proxy for the entropy the whole security argument rests on — a regression
    /// that shortened the token would otherwise pass every other test in this file.
    /// </summary>
    [Fact]
    public void CreateToken_Is64LowercaseHexCharacters()
    {
        var token = _sut.CreateToken();

        token.Should().HaveLength(64);
        token.Should().MatchRegex("^[0-9a-f]+$", "the token is pasted into a WhatsApp message, where '_' and '-' trigger formatting and break link detection");
    }

    [Fact]
    public void CreateToken_NeverRepeats()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => _sut.CreateToken()).ToList();

        tokens.Distinct().Should().HaveCount(500);
    }

    [Fact]
    public void Hash_IsDeterministic_SoLookupByHashWorks()
    {
        var token = _sut.CreateToken();

        _sut.Hash(token).Should().Be(_sut.Hash(token));
    }

    [Fact]
    public void Hash_DoesNotContainTheToken_AndIsAFixedWidthDigest()
    {
        var token = _sut.CreateToken();

        var hash = _sut.Hash(token);

        hash.Should().HaveLength(64); // SHA-256, hex
        hash.Should().MatchRegex("^[0-9a-f]+$");
        hash.Should().NotContain(token);
    }

    [Fact]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        _sut.Hash(_sut.CreateToken()).Should().NotBe(_sut.Hash(_sut.CreateToken()));
    }
}
