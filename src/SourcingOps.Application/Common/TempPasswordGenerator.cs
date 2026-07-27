using System.Security.Cryptography;

namespace SourcingOps.Application.Common;

/// <summary>
/// Generates temp passwords per TECH_SPEC §4.4: crypto RNG, 12 characters, mixed
/// alphanumeric + one symbol, excludes visually ambiguous characters (0/O, 1/l).
/// Used by the E1-03 bootstrap admin seed now, and by E11-01/E11-02 (create user /
/// reset password) later.
/// </summary>
public static class TempPasswordGenerator
{
    private const string Letters = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz"; // no I/l/O confusable set
    private const string Digits = "23456789"; // no 0/1
    private const string Symbols = "!@#$%^&*-_=+?";

    public static string Generate(int length = 12)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must allow at least one letter, digit and symbol.");
        }

        var alphabet = Letters + Digits;
        var chars = new char[length];

        for (var i = 0; i < length - 1; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        // Guarantee exactly one symbol, placed at a random position, per the spec's "mixed alphanumeric + one symbol".
        chars[length - 1] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        // Shuffle so the symbol isn't always last (Fisher-Yates using crypto RNG).
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
