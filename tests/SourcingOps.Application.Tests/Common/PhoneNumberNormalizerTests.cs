using FluentAssertions;
using SourcingOps.Application.Common;

namespace SourcingOps.Application.Tests.Common;

/// <summary>Covers ACTION_PLAN E4-04 / FR-CRM-04: normalised international phone storage.</summary>
public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+91 98250 41122", "+91", "+919825041122")]
    [InlineData("098250-41122", "+91", "+919825041122")]
    [InlineData("9825041122", "+91", "+919825041122")]
    [InlineData("0091 98250 41122", "+91", "+919825041122")] // "00" international trunk prefix + full country code, as actually dialed
    [InlineData("+919825041122", "+91", "+919825041122")]
    public void Normalize_VariousLooseInputs_ProduceTheSameE164Value(string input, string defaultCountryCode, string expected)
    {
        PhoneNumberNormalizer.Normalize(input, defaultCountryCode).Should().Be(expected);
    }

    [Fact]
    public void Normalize_ChineseNumberWithExplicitCountryCode_IsNotForcedToTheDefaultCountry()
    {
        // A vendor's Chinese number (E5, later milestone) must not be mangled by a default
        // country code meant for Indian customers (E4).
        var result = PhoneNumberNormalizer.Normalize("+86 138 0013 8000", "+91");

        result.Should().Be("+8613800138000");
    }

    [Fact]
    public void Normalize_DefaultCountryCodeIsConfigurable_NotHardCoded()
    {
        var result = PhoneNumberNormalizer.Normalize("5551234567", "+1");

        result.Should().Be("+15551234567");
    }

    [Fact]
    public void Normalize_ResultConcatenatesDirectlyIntoAWaMeLink_WithNoFurtherTransformation()
    {
        // ACTION_PLAN E9-01 depends on this: the normalized value must be usable to build a
        // wa.me deep link with no further transformation — i.e. simply strip the leading '+'.
        var normalized = PhoneNumberNormalizer.Normalize("+91 98250 41122", "+91");

        var waMeLink = $"https://wa.me/{normalized.TrimStart('+')}";

        waMeLink.Should().Be("https://wa.me/919825041122");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrWhitespaceInput_ThrowsValidationException(string? input)
    {
        var act = () => PhoneNumberNormalizer.Normalize(input, "+91");

        act.Should().Throw<AppValidationException>();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("++++")]
    [InlineData("1")]
    public void Normalize_UnrecognisableInput_ThrowsValidationException(string input)
    {
        var act = () => PhoneNumberNormalizer.Normalize(input, "+91");

        act.Should().Throw<AppValidationException>();
    }
}
