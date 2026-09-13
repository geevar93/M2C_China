using System.Globalization;

namespace SourcingOps.Application.Common;

/// <summary>
/// Formats a line quantity for display on a server-composed document.
///
/// Like <see cref="MoneyFormatter"/>, this deliberately uses
/// <see cref="CultureInfo.InvariantCulture"/> and NEVER a named culture: the API's runtime image
/// ships without ICU and runs in globalization-invariant mode, where any named-culture lookup
/// throws. See <see cref="MoneyFormatter"/>'s doc for the full account of how that surfaced.
/// </summary>
public static class QuantityFormatter
{
    /// <summary>
    /// Trims insignificant trailing zeros so whole quantities read as <c>10</c> rather than
    /// <c>10.000</c>, while a fractional one keeps the precision it was entered with
    /// (<c>2.5</c>, <c>0.125</c>). The column is stored at 3dp, so that is the cap.
    /// </summary>
    public static string Format(decimal quantity)
    {
        var rounded = Math.Round(quantity, 3, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
