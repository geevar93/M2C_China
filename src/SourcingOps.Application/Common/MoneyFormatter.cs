using System.Globalization;

namespace SourcingOps.Application.Common;

/// <summary>
/// Server-side counterpart of the client's <c>money.util.ts</c> (N-30). Every money string
/// composed on the SERVER — a PDF line item, a timeline body — must go through here so it
/// matches the client's <c>Intl.NumberFormat('en-IN', …)</c> grouping instead of drifting into
/// a plain <c>0.00</c> format. INR prints as the <c>₹</c> symbol with no space
/// (<c>₹1,47,500.00</c>), exactly as the client's <c>formatInr</c> does; any other currency keeps
/// its ISO code prefix (<c>USD 1,000.00</c>).
///
/// The ₹ glyph is safe in the invoice PDF: the Lato family bundled with QuestPDF (the PDF's
/// default font, and the only font inside the Alpine container) includes U+20B9 in its regular,
/// bold and italic faces. <c>QuestPdfInvoiceRendererTests</c> renders with system fonts
/// disabled so that stays verified if the font or QuestPDF version ever changes.
/// </summary>
public static class MoneyFormatter
{
    /// <summary>
    /// Indian lakh/crore digit grouping (e.g. <c>1,47,500.00</c>), pinned explicitly via
    /// <see cref="NumberFormatInfo.NumberGroupSizes"/> rather than trusted to <c>en-IN</c>'s
    /// default ICU data, since that data can differ across machines/containers.
    ///
    /// Built from <see cref="CultureInfo.InvariantCulture"/>, NOT <c>CultureInfo.GetCultureInfo("en-IN")</c>.
    /// The runtime image (<c>mcr.microsoft.com/dotnet/aspnet:10.0-alpine</c>) ships without ICU and
    /// therefore runs in globalization-invariant mode, where looking up ANY named culture throws
    /// <see cref="CultureNotFoundException"/> — inside a static constructor that surfaces as a
    /// <see cref="TypeInitializationException"/> and 500s the request. It did exactly that on the
    /// first DRAFT→ISSUED transition, because the PDF renderer is the first caller to touch this
    /// type. Tests on a Windows dev box could not catch it: ICU is present there, so en-IN resolves.
    ///
    /// Nothing is lost by dropping en-IN. The only two properties that matter here are already
    /// identical between en-IN and the invariant culture (<c>.</c> decimal separator, <c>,</c> group
    /// separator), and the grouping itself is overridden on the next line regardless — en-IN was
    /// never actually supplying the Indian grouping, only a starting point.
    /// </summary>
    private static readonly NumberFormatInfo IndianGrouping = CreateIndianGrouping();

    private static NumberFormatInfo CreateIndianGrouping()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSizes = [3, 2];
        return format;
    }

    /// <summary>
    /// <c>("INR", 147500.00m)</c> -&gt; <c>"₹1,47,500.00"</c>; <c>("USD", 1000m)</c> -&gt; <c>"USD 1,000.00"</c>.
    /// A negative INR amount puts the sign before the symbol (<c>-₹100.00</c>), as <c>Intl</c> does.
    /// </summary>
    public static string Format(string currency, decimal amount)
    {
        if (string.Equals(currency, "INR", StringComparison.OrdinalIgnoreCase))
        {
            var sign = amount < 0 ? "-" : string.Empty;
            return $"{sign}₹{Math.Abs(amount).ToString("N2", IndianGrouping)}";
        }

        return $"{currency} {amount.ToString("N2", IndianGrouping)}";
    }
}
