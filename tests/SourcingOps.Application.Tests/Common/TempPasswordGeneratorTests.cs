using FluentAssertions;
using SourcingOps.Application.Common;

namespace SourcingOps.Application.Tests.Common;

public class TempPasswordGeneratorTests
{
    [Fact]
    public void Generate_ProducesRequestedLength()
    {
        var password = TempPasswordGenerator.Generate(12);

        password.Should().HaveLength(12);
    }

    [Fact]
    public void Generate_ExcludesAmbiguousCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TempPasswordGenerator.Generate();
            password.Should().NotContainAny("0", "O", "1", "l", "I");
        }
    }

    [Fact]
    public void Generate_ContainsAtLeastOneSymbol()
    {
        var password = TempPasswordGenerator.Generate();

        password.Should().MatchRegex("[!@#$%^&*\\-_=+?]");
    }

    [Fact]
    public void Generate_ProducesDifferentValuesEachCall()
    {
        var first = TempPasswordGenerator.Generate();
        var second = TempPasswordGenerator.Generate();

        first.Should().NotBe(second);
    }

    [Fact]
    public void Generate_WithTooSmallLength_Throws()
    {
        var act = () => TempPasswordGenerator.Generate(2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
