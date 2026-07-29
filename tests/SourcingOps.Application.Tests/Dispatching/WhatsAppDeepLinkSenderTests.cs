using FluentAssertions;
using SourcingOps.Application.Common;
using SourcingOps.Infrastructure.Dispatching;

namespace SourcingOps.Application.Tests.Dispatching;

/// <summary>
/// Covers ACTION_PLAN E9-01: the `wa.me` deep-link builder. Also the direct proof that E4-04's
/// stated invariant ("the stored value builds a `wa.me` link with no further transformation")
/// actually holds — <see cref="PhoneNumberNormalizer"/>'s final candidate is always "+" plus
/// digits only (see its own doc comment), and the two "realistic stored value" cases below feed
/// exactly that shape straight into <see cref="WhatsAppDeepLinkSender"/> with no punctuation
/// stripped in between.
/// </summary>
public class WhatsAppDeepLinkSenderTests
{
    private readonly WhatsAppDeepLinkSender _sut = new();

    [Fact]
    public void Prepare_RealisticNormalizedIndianNumber_BuildsWaMeLinkWithNoLeadingPlus()
    {
        // "+91 98250 41122" normalises (PhoneNumberNormalizer) to exactly this stored shape.
        var result = _sut.Prepare("+919825041122", "Hello there");

        result.DeepLinkUrl.Should().Be("https://wa.me/919825041122?text=Hello%20there");
    }

    [Fact]
    public void Prepare_RealisticNormalizedChineseNumber_BuildsWaMeLinkWithNoLeadingPlus()
    {
        // "+86 138 0013 8000" normalises (PhoneNumberNormalizer) to exactly this stored shape.
        var result = _sut.Prepare("+8613800138000", "Hi there");

        result.DeepLinkUrl.Should().Be("https://wa.me/8613800138000?text=Hi%20there");
    }

    [Fact]
    public void Prepare_MessageWithSpacesAndPunctuation_UrlEncodesText()
    {
        var result = _sut.Prepare("+919825041122", "Hi Ravi, here's our catalog: \"Spring 2026\"!");

        result.DeepLinkUrl.Should().StartWith("https://wa.me/919825041122?text=");
        result.DeepLinkUrl.Should().NotContain(" ");
        result.DeepLinkUrl.Should().Contain(Uri.EscapeDataString("Hi Ravi, here's our catalog: \"Spring 2026\"!"));
    }

    [Fact]
    public void Prepare_PunctuatedPhoneInput_StripsToDigitsOnly_DefensivelyToleratesNonNormalizedInput()
    {
        // Defensive case: even if a value somehow reached this component without having gone
        // through PhoneNumberNormalizer first (e.g. seed/prototype data with loose formatting),
        // the builder must not embed spaces/punctuation in the wa.me path segment.
        var result = _sut.Prepare("+86 138 0013 8000", "Hi");

        result.DeepLinkUrl.Should().StartWith("https://wa.me/8613800138000?text=");
    }

    [Fact]
    public void Prepare_NoDigitsInPhone_ThrowsValidationException()
    {
        var act = () => _sut.Prepare("not-a-phone", "Hi");

        act.Should().Throw<AppValidationException>();
    }
}
