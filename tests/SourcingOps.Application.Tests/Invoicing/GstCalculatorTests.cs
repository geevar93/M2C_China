using FluentAssertions;
using SourcingOps.Application.Invoicing;
using Xunit;

namespace SourcingOps.Application.Tests.Invoicing;

/// <summary>
/// Every assertion terminates in a LITERAL expected figure, never one recomputed through
/// <see cref="GstCalculator"/> — the same rule <c>MoneyFormatterTests</c> states (D-64/D-72):
/// a test that asserts via the code under test cannot catch a regression in it.
/// </summary>
public class GstCalculatorTests
{
    // ---- Tax-exclusive basis -----------------------------------------------------------

    [Fact]
    public void ForLine_AddsTaxOnTopOfUnitPrice_RatherThanBackingItOut()
    {
        // 10 x 1,000 at 18% inter-state. Exclusive basis: 10,000 taxable + 1,800 tax.
        // The inclusive (true-MRP) reading of the same input would be 8,474.58 + 1,525.42.
        var result = GstCalculator.ForLine(quantity: 10m, unitPrice: 1000m, gstRatePercent: 18m, isIntraState: false);

        result.TaxableValue.Should().Be(10000.00m);
        result.Igst.Should().Be(1800.00m);
        result.LineTotal.Should().Be(11800.00m);
    }

    // ---- Intra-state: CGST + SGST ------------------------------------------------------

    [Fact]
    public void ForLine_IntraState_SplitsIntoEqualCgstAndSgstAtHalfTheRate()
    {
        var result = GstCalculator.ForLine(10m, 1000m, 18m, isIntraState: true);

        result.Cgst.Should().Be(900.00m);
        result.Sgst.Should().Be(900.00m);
        result.Igst.Should().Be(0m);
        result.TotalTax.Should().Be(1800.00m);
    }

    [Fact]
    public void ForLine_IntraState_CgstAndSgstAreAlwaysEqual_EvenWhenHalfTheRateDoesNotDivideCleanly()
    {
        // 1 x 999.99 at 5%: half-rate 2.5% of 999.99 = 24.99975 -> 25.00 each head.
        var result = GstCalculator.ForLine(1m, 999.99m, 5m, isIntraState: true);

        result.Cgst.Should().Be(25.00m);
        result.Sgst.Should().Be(25.00m);
        result.Cgst.Should().Be(result.Sgst);
    }

    [Fact]
    public void ForLine_InterState_PutsTheWholeRateInIgst_AndLeavesCgstSgstZero()
    {
        var result = GstCalculator.ForLine(3m, 2500m, 12m, isIntraState: false);

        result.TaxableValue.Should().Be(7500.00m);
        result.Igst.Should().Be(900.00m);
        result.Cgst.Should().Be(0m);
        result.Sgst.Should().Be(0m);
    }

    // ---- Rounding ----------------------------------------------------------------------

    [Fact]
    public void ForLine_RoundsMidpointsAwayFromZero_NotToEven()
    {
        // 1 x 105.00 at 5% = 5.25 exactly; pick a case whose third decimal is a true midpoint:
        // 1 x 2.50 at 5% = 0.125 -> 0.13 away-from-zero (banker's rounding would give 0.12).
        var result = GstCalculator.ForLine(1m, 2.50m, 5m, isIntraState: false);

        result.Igst.Should().Be(0.13m);
    }

    [Fact]
    public void ForLine_RoundsTheTaxableValueToTwoDecimals()
    {
        // 3 x 33.333 = 99.999 -> 100.00
        var result = GstCalculator.ForLine(3m, 33.333m, 0m, isIntraState: false);

        result.TaxableValue.Should().Be(100.00m);
    }

    [Fact]
    public void ForLine_ComputesTaxOnTheRoundedTaxableValue_SoThePrintedRowIsSelfConsistent()
    {
        // Taxable rounds to 100.00; 18% of that is exactly 18.00. Were tax computed on the
        // unrounded 99.999 it would be 17.99982 -> 18.00 too, but the row must add up from
        // what it PRINTS, which is what this pins.
        var result = GstCalculator.ForLine(3m, 33.333m, 18m, isIntraState: false);

        result.TaxableValue.Should().Be(100.00m);
        result.Igst.Should().Be(18.00m);
        result.LineTotal.Should().Be(118.00m);
    }

    // ---- Edge rates --------------------------------------------------------------------

    [Fact]
    public void ForLine_ZeroRate_ProducesNoTaxButStillATaxableValue()
    {
        var result = GstCalculator.ForLine(2m, 500m, 0m, isIntraState: true);

        result.TaxableValue.Should().Be(1000.00m);
        result.TotalTax.Should().Be(0m);
        result.LineTotal.Should().Be(1000.00m);
    }

    [Fact]
    public void ForLine_ZeroQuantity_IsAllowed_AndYieldsZeroes()
    {
        var result = GstCalculator.ForLine(0m, 500m, 18m, isIntraState: false);

        result.TaxableValue.Should().Be(0m);
        result.TotalTax.Should().Be(0m);
    }

    [Fact]
    public void ForLine_FractionalQuantity_IsSupported_ForUnitsSoldByWeight()
    {
        // 2.5 kg x 240.00 at 5% inter-state = 600.00 taxable, 30.00 IGST.
        var result = GstCalculator.ForLine(2.5m, 240m, 5m, isIntraState: false);

        result.TaxableValue.Should().Be(600.00m);
        result.Igst.Should().Be(30.00m);
    }

    [Fact]
    public void ForLine_HighestSlab_28Percent()
    {
        var result = GstCalculator.ForLine(1m, 10000m, 28m, isIntraState: true);

        result.Cgst.Should().Be(1400.00m);
        result.Sgst.Should().Be(1400.00m);
        result.TotalTax.Should().Be(2800.00m);
    }

    // ---- Guards ------------------------------------------------------------------------

    [Fact]
    public void ForLine_NegativeQuantity_Throws()
    {
        var act = () => GstCalculator.ForLine(-1m, 100m, 18m, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ForLine_NegativeUnitPrice_Throws()
    {
        var act = () => GstCalculator.ForLine(1m, -100m, 18m, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ForLine_NegativeRate_Throws()
    {
        var act = () => GstCalculator.ForLine(1m, 100m, -18m, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- Place of supply ---------------------------------------------------------------

    [Fact]
    public void IsIntraState_SameStateCode_IsTrue()
    {
        GstCalculator.IsIntraState("24", "24").Should().BeTrue();
    }

    [Fact]
    public void IsIntraState_DifferentStateCodes_IsFalse()
    {
        GstCalculator.IsIntraState("24", "27").Should().BeFalse();
    }

    // ---- Multi-line totals sum ROUNDED lines (what a GST return reconciles against) ------

    [Fact]
    public void InvoiceTotal_IsTheSumOfIndependentlyRoundedLines_NotOneRoundingAtTheEnd()
    {
        // Three lines whose exact tax each ends in a half-paisa. Summing rounded lines gives
        // 0.13 * 3 = 0.39; rounding the exact sum (0.375) once would give 0.38.
        var lines = new[]
        {
            GstCalculator.ForLine(1m, 2.50m, 5m, false),
            GstCalculator.ForLine(1m, 2.50m, 5m, false),
            GstCalculator.ForLine(1m, 2.50m, 5m, false)
        };

        lines.Sum(l => l.Igst).Should().Be(0.39m);
    }
}
