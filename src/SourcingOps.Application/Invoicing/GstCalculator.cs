namespace SourcingOps.Application.Invoicing;

/// <summary>The computed tax breakdown of a single invoice line.</summary>
/// <param name="TaxableValue">Quantity * UnitPrice, rounded to 2dp, less <paramref name="Discount"/>. GST excluded.</param>
/// <param name="Cgst">Central GST — non-zero only on an intra-state supply.</param>
/// <param name="Sgst">State GST — always equal to <paramref name="Cgst"/>, non-zero only intra-state.</param>
/// <param name="Igst">Integrated GST — non-zero only on an inter-state supply.</param>
/// <param name="Discount">The line discount already taken off <paramref name="TaxableValue"/>.</param>
public readonly record struct GstLineAmounts(decimal TaxableValue, decimal Cgst, decimal Sgst, decimal Igst, decimal Discount = 0m)
{
    /// <summary>Quantity * UnitPrice before the discount.</summary>
    public decimal GrossValue => TaxableValue + Discount;

    public decimal TotalTax => Cgst + Sgst + Igst;
    public decimal LineTotal => TaxableValue + TotalTax;
}

/// <summary>
/// Indian GST arithmetic for one invoice line. Pure and dependency-free so the rules are
/// testable in isolation — no database, no clock, no culture.
///
/// The basis is TAX-EXCLUSIVE: <c>UnitPrice</c> is a net selling price and GST is added on top.
/// This is deliberately NOT the legal-MRP treatment, where the printed price already includes
/// GST and the taxable value has to be backed out with <c>gross * 100 / (100 + rate)</c>. The
/// two produce materially different numbers from the same input — at 18%, a 1,000 figure is
/// either 1,180 total (exclusive) or 1,000 total on a 847.46 base (inclusive) — so the basis is
/// stated here rather than left to the caller to assume. If a true MRP basis is ever needed it
/// belongs alongside this method as a second, explicitly named one, never as a silent change
/// to this.
///
/// ROUNDING: each line is rounded to 2dp independently and the invoice total is the sum of
/// rounded lines, rather than rounding once at the end. This is what GST returns reconcile
/// against, and it keeps a line's printed figures self-consistent — a line that prints
/// 8,474.58 + 1,525.42 must total 10,000.00 on its own row. Midpoints round away from zero
/// (0.005 -> 0.01), not to-even, which is banker's rounding and would under-report tax on
/// exactly-half paise.
///
/// INTRA-STATE SPLIT: CGST and SGST are each half the rate. The half is computed on the rate,
/// not by halving the total tax, and SGST is then taken as exactly equal to CGST rather than
/// rounded separately — that guarantees CGST + SGST equals the round-2dp of the full-rate tax
/// and cannot leave a one-paisa discrepancy between the two heads.
/// </summary>
public static class GstCalculator
{
    /// <summary>The statutory slabs. Used for validation messaging, not to restrict arithmetic.</summary>
    public static readonly decimal[] StandardRates = [0m, 0.25m, 3m, 5m, 12m, 18m, 28m];

    /// <param name="quantity">Line quantity. Must not be negative.</param>
    /// <param name="unitPrice">Per-unit price, GST EXCLUSIVE. Must not be negative.</param>
    /// <param name="gstRatePercent">Rate as a percent — <c>18m</c> for 18%, not <c>0.18m</c>.</param>
    /// <param name="isIntraState">True splits into CGST + SGST; false yields a single IGST amount.</param>
    /// <param name="discount">Already-resolved discount amount (see <see cref="LineDiscount"/>), taken off before GST.</param>
    public static GstLineAmounts ForLine(decimal quantity, decimal unitPrice, decimal gstRatePercent, bool isIntraState, decimal discount = 0m)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (gstRatePercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gstRatePercent), "GST rate cannot be negative.");
        }

        var grossValue = Round(quantity * unitPrice);
        if (discount < 0 || discount > grossValue)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount must be between zero and the line's gross value.");
        }

        var taxableValue = grossValue - discount;

        if (isIntraState)
        {
            // Half the RATE, not half the tax — see the class doc on why the two are not
            // interchangeable once rounding is involved.
            var cgst = Round(taxableValue * (gstRatePercent / 2m) / 100m);
            return new GstLineAmounts(taxableValue, cgst, cgst, 0m, discount);
        }

        var igst = Round(taxableValue * gstRatePercent / 100m);
        return new GstLineAmounts(taxableValue, 0m, 0m, igst, discount);
    }

    /// <summary>
    /// The discount amount for one line. A percent applies to the line's rounded gross value and
    /// is itself rounded to 2dp; a flat amount is taken as entered. No discount type means zero.
    /// Range checks belong to the caller (the request validator) — this only does arithmetic.
    /// </summary>
    public static decimal LineDiscount(decimal quantity, decimal unitPrice, string? discountType, decimal? discountValue)
    {
        var value = discountValue ?? 0m;
        return discountType switch
        {
            InvoiceDiscountTypes.Percent => Round(Round(quantity * unitPrice) * value / 100m),
            InvoiceDiscountTypes.Amount => value,
            _ => 0m
        };
    }

    /// <summary>
    /// Place of supply: an intra-state supply is one where the buyer's state code equals the
    /// seller's. Both are required — a null on either side is NOT treated as "assume
    /// intra-state", because that assumption silently halves the rate on what may be an
    /// inter-state supply. Callers must reject the invoice instead.
    /// </summary>
    public static bool IsIntraState(string sellerStateCode, string buyerStateCode) =>
        string.Equals(sellerStateCode, buyerStateCode, StringComparison.Ordinal);

    /// <summary>Money rounding: 2dp, midpoints away from zero (see the class doc).</summary>
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
