using System.Text.RegularExpressions;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Dispatching;

/// <summary>
/// ACTION_PLAN E9-01: builds a `wa.me` click-to-chat deep link from a stored international
/// phone number and pre-filled text. No WhatsApp Business API dependency — FSD A2 is an
/// explicit Phase 1 non-goal, and this class talks to nothing external; it only formats a URL
/// string. Sits behind <see cref="IDispatchMessageSender"/> (E9-09) so a future Business-API
/// sender can replace it without touching <c>DispatchService</c>.
///
/// `wa.me/&lt;number&gt;` requires the number with no leading `+`, no spaces, and no
/// punctuation. <see cref="Application.Common.PhoneNumberNormalizer"/>'s stated invariant is
/// that <c>Customer.Phone</c> is always stored as "+" followed only by digits (its final
/// candidate is built as <c>"+" + digitsOnly</c> and validated against a strict E.164 regex
/// before being accepted) — so in practice this reduces to stripping one leading character.
/// This builder still strips every non-digit character defensively rather than assuming that
/// invariant holds for every possible caller (e.g. data that reached the column by a path other
/// than the normaliser), which is also what makes it tolerant of the loosely-formatted
/// "+86 138 0013 8000" / "+91 98250 41122" style values the seed/prototype data uses.
/// </summary>
public sealed class WhatsAppDeepLinkSender : IDispatchMessageSender
{
    private static readonly Regex NonDigits = new(@"\D+", RegexOptions.Compiled);

    public DispatchSendPreparation Prepare(string customerPhoneE164, string message)
    {
        var digitsOnly = NonDigits.Replace(customerPhoneE164 ?? string.Empty, string.Empty);
        if (digitsOnly.Length == 0)
        {
            throw new AppValidationException("customerPhone", $"'{customerPhoneE164}' has no usable digits to build a wa.me link from.");
        }

        var encodedText = Uri.EscapeDataString(message ?? string.Empty);
        var url = $"https://wa.me/{digitsOnly}?text={encodedText}";
        return new DispatchSendPreparation(url);
    }
}
