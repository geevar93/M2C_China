using System.Globalization;

namespace SourcingOps.Application.Common;

/// <summary>
/// Server-side counterpart of the client's <c>money.util.ts</c> (N-30). Every money string
/// composed on the SERVER — a PDF line item, a timeline body — must go through here so it
/// matches the client's <c>Intl.NumberFormat('en-IN', …)</c> grouping instead of drifting into
/// a plain <c>0.00</c> format. Keeps the ISO currency code prefix (e.g. <c>"INR"</c>), never the
/// <c>₹</c> glyph: on a tax invoice the ISO code is the correct label, and QuestPDF's default
/// font has no glyph for it, which would render as a blank box on a document sent to customers.
/// </summary>
public static class MoneyFormatter
{
    /// <summary>
    /// Indian lakh/crore digit grouping (2,2,3 — e.g. <c>1,47,500.00</c>), pinned explicitly via
    /// <see cref="NumberFormatInfo.NumberGroupSizes"/> rather than trusted to <c>en-IN</c>'s
    /// default ICU data, since that data can differ across machines/containers. Verified on the
    /// dev machine that plain <c>CultureInfo("en-IN")</c> already groups correctly, but pinning
    /// it removes the dependency on that holding true everywhere this runs.
    /// </summary>
    private static readonly NumberFormatInfo IndianGrouping = CreateIndianGrouping();

    private static NumberFormatInfo CreateIndianGrouping()
    {
        var format = (NumberFormatInfo)CultureInfo.GetCultureInfo("en-IN").NumberFormat.Clone();
        format.NumberGroupSizes = [3, 2];
        return format;
    }

    /// <summary><c>("INR", 147500.00m)</c> -&gt; <c>"INR 1,47,500.00"</c>.</summary>
    public static string Format(string currency, decimal amount) =>
        $"{currency} {amount.ToString("N2", IndianGrouping)}";
}
