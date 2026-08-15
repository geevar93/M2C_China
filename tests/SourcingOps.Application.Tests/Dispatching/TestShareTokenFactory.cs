using SourcingOps.Application.Interfaces;

namespace SourcingOps.Application.Tests.Dispatching;

/// <summary>
/// E9-10: a deterministic, sequential <see cref="IShareTokenFactory"/> for tests that need to
/// know the token that was minted (so they can then fetch with it). The production
/// implementation's randomness is exactly what makes it untestable this way — and is itself
/// covered by <c>Sha256ShareTokenFactoryTests</c>, which asserts the properties that matter
/// (length, uniqueness, URL-safety, hash stability) rather than any specific value.
///
/// The sequential counter is deliberately the opposite of what production does — if this type
/// were ever registered outside a test, every share URL would be guessable, which is the
/// failure this comment exists to make obvious to anyone reading it in a DI file.
/// </summary>
public sealed class TestShareTokenFactory : IShareTokenFactory
{
    private int _counter;

    public string LastToken { get; private set; } = string.Empty;

    public string CreateToken()
    {
        LastToken = $"test-token-{Interlocked.Increment(ref _counter)}";
        return LastToken;
    }

    public string Hash(string token) => $"hash::{token}";
}
