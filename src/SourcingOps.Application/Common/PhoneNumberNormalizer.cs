using System.Text.RegularExpressions;

namespace SourcingOps.Application.Common;

/// <summary>
/// Normalises loose phone input into E.164 (FR-CRM-04, ACTION_PLAN E4-04) — e.g.
/// "+91 98250 41122", "098250-41122" and "9825041122" all normalise to "+919825041122" given a
/// default country code of "+91". The output is directly usable to build a `wa.me` link with
/// no further transformation (ACTION_PLAN E9-01 depends on this — see the class doc comment on
/// <see cref="Crm.CustomerOptions"/>).
/// </summary>
public static class PhoneNumberNormalizer
{
    // E.164: '+' followed by 8-15 digits, first digit 1-9 (no leading zero after the +).
    private static readonly Regex E164Pattern = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    public static string Normalize(string? rawInput, string defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            throw new AppValidationException("phone", "Phone number is required.");
        }

        var trimmed = rawInput.Trim();
        var hasLeadingPlus = trimmed.StartsWith('+');
        var digitsOnly = new string(trimmed.Where(char.IsDigit).ToArray());

        if (digitsOnly.Length == 0)
        {
            throw new AppValidationException("phone", $"'{rawInput}' is not a valid phone number.");
        }

        var normalizedCountryCode = NormalizeCountryCodePrefix(defaultCountryCode);
        string candidate;

        if (hasLeadingPlus)
        {
            // Already carries an explicit country code.
            candidate = "+" + digitsOnly;
        }
        else if (trimmed.StartsWith("00"))
        {
            // International trunk prefix (e.g. "0086 138...") — '00' stands in for '+'.
            candidate = "+" + digitsOnly[2..];
        }
        else
        {
            // No country code present. Strip a single national trunk-prefix '0'
            // (e.g. India's leading "0" before an STD/mobile number) if present, then apply
            // the configured default country code.
            var national = digitsOnly.Length > 1 && digitsOnly.StartsWith('0') ? digitsOnly[1..] : digitsOnly;
            candidate = normalizedCountryCode + national;
        }

        if (!E164Pattern.IsMatch(candidate))
        {
            throw new AppValidationException("phone", $"'{rawInput}' does not normalise to a valid international phone number.");
        }

        return candidate;
    }

    private static string NormalizeCountryCodePrefix(string defaultCountryCode)
    {
        var digits = new string((defaultCountryCode ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? "+91" : "+" + digits;
    }
}
