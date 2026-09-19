using FluentAssertions;
using SourcingOps.Application.Common;
using Xunit;

namespace SourcingOps.Application.Tests.Common;

/// <summary>
/// N-30: every server-composed money string must match the client's <c>money.util.ts</c> Indian
/// lakh/crore grouping instead of a plain <c>0.00</c> format. Per D-64/D-72's sibling rule, every
/// assertion here terminates in a LITERAL string, never in a value recomputed through
/// <see cref="MoneyFormatter"/> itself — a test that asserts via the code under test cannot
/// catch a regression in it.
/// </summary>
public class MoneyFormatterTests
{
    [Fact]
    public void Format_SixDigitAmount_UsesIndianLakhGrouping_NotWesternThousandsGrouping()
    {
        MoneyFormatter.Format("INR", 147500.00m).Should().Be("₹1,47,500.00");
    }

    [Fact]
    public void Format_CroreRangeAmount_GroupsCorrectly()
    {
        MoneyFormatter.Format("INR", 12347500.00m).Should().Be("₹1,23,47,500.00");
    }

    [Fact]
    public void Format_SmallAmount_NoGroupingNeeded()
    {
        MoneyFormatter.Format("INR", 180.00m).Should().Be("₹180.00");
    }

    [Fact]
    public void Format_AmountBelowOneThousand_LessThanTheFirstGroupBoundary()
    {
        MoneyFormatter.Format("INR", 999.99m).Should().Be("₹999.99");
    }

    [Fact]
    public void Format_Inr_UsesTheRupeeSymbol_NotTheIsoCode()
    {
        MoneyFormatter.Format("INR", 100m).Should().Be("₹100.00");
    }

    [Fact]
    public void Format_NegativeInr_PutsTheSignBeforeTheSymbol()
    {
        MoneyFormatter.Format("INR", -1500m).Should().Be("-₹1,500.00");
    }

    [Fact]
    public void Format_OtherCurrency_KeepsItsIsoCodePrefix()
    {
        MoneyFormatter.Format("USD", 1000m).Should().Be("USD 1,000.00");
    }

    [Fact]
    public void Format_AlwaysTwoDecimalPlaces_EvenForAWholeNumber()
    {
        MoneyFormatter.Format("INR", 5000m).Should().Be("₹5,000.00");
    }
}
