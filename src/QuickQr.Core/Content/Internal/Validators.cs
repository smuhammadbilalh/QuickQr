using System.Text;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Field validators and normalisers (email, phone, IBAN, GTIN check digits).</summary>
internal static partial class Validators
{
    [GeneratedRegex(@"^[^\s@,;:<>()\[\]\\""]{1,64}@(?:[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?\.)+[\p{L}\p{N}-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    public static bool IsEmail(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 254 && EmailRegex().IsMatch(value);

    /// <summary>Splits an address list on commas/semicolons and trims each entry.</summary>
    public static List<string> SplitAddresses(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    // ---- Phone numbers ----

    private static bool IsPhoneSeparator(char c) =>
        char.IsWhiteSpace(c) || c is '-' or '.' or '(' or ')' or '/' or '‐' or '‑' or '‒' or '–';

    /// <summary>
    /// Normalises a phone number to an optional leading '+' followed by digits, removing spaces, dashes, dots,
    /// slashes and parentheses. Returns null when other characters are present or the digit count is implausible.
    /// </summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sb = new StringBuilder(value.Length);
        var digits = 0;
        foreach (var c in value.Trim())
        {
            if (char.IsAsciiDigit(c))
            {
                sb.Append(c);
                digits++;
            }
            else if (c == '+' && sb.Length == 0)
            {
                sb.Append(c);
            }
            else if (!IsPhoneSeparator(c))
            {
                return null;
            }
        }
        return digits is >= 3 and <= 20 ? sb.ToString() : null;
    }

    /// <summary>Best-effort normalisation that never fails: keeps a leading '+' and the digits.</summary>
    public static string StripPhone(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsAsciiDigit(c)) sb.Append(c);
            else if (c == '+' && sb.Length == 0) sb.Append(c);
        }
        return sb.ToString();
    }

    public static string DigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    public static int CountDigits(string value)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c)) count++;
        }
        return count;
    }

    public static bool IsAllDigits(string value)
    {
        if (value.Length == 0) return false;
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    // ---- IBAN (ISO 13616, mod 97-10) ----

    public static string NormalizeIban(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    public static bool IsValidIban(string iban)
    {
        if (iban.Length is < 15 or > 34) return false;
        if (!char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1]) ||
            !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
            return false;

        var remainder = 0;
        for (var i = 0; i < iban.Length; i++)
        {
            // Move the first four characters to the end, then interpret letters as 10..35.
            var c = iban[(i + 4) % iban.Length];
            if (char.IsAsciiDigit(c)) remainder = ((remainder * 10) + (c - '0')) % 97;
            else if (char.IsAsciiLetterUpper(c)) remainder = ((remainder * 100) + (c - 'A' + 10)) % 97;
            else return false;
        }
        return remainder == 1;
    }

    /// <summary>Groups an IBAN in blocks of four for display.</summary>
    public static string FormatIban(string iban)
    {
        var sb = new StringBuilder(iban.Length + (iban.Length / 4));
        for (var i = 0; i < iban.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(iban[i]);
        }
        return sb.ToString();
    }

    // ---- GTIN / EAN / UPC check digits ----

    /// <summary>Validates the GS1 mod-10 check digit of an all-digit GTIN-8/12/13/14.</summary>
    public static bool IsValidGtin(string digits)
    {
        if (digits.Length < 2 || !IsAllDigits(digits)) return false;
        var sum = 0;
        var weight = 3;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = 4 - weight;
        }
        return (10 - (sum % 10)) % 10 == digits[^1] - '0';
    }

    /// <summary>Expands an 8-digit UPC-E code (number system + 6 digits + check) to UPC-A, or null when not UPC-E.</summary>
    public static string? ExpandUpcE(string upcE)
    {
        if (upcE.Length != 8 || !IsAllDigits(upcE) || upcE[0] is not ('0' or '1')) return null;
        var d = upcE.AsSpan(1, 6);
        var body = d[5] switch
        {
            '0' or '1' or '2' => string.Concat(d[..2], d[5..6], "0000", d[2..5]),
            '3' => string.Concat(d[..3], "00000", d[3..5]),
            '4' => string.Concat(d[..4], "00000", d[4..5]),
            _ => string.Concat(d[..5], "0000", d[5..6]),
        };
        return string.Concat(upcE[..1], body, upcE[7..]);
    }

    /// <summary>True for an EAN-8, UPC-E, UPC-A or EAN-13 code with a valid check digit.</summary>
    public static bool IsValidProductCode(string digits)
    {
        if (!IsAllDigits(digits)) return false;
        return digits.Length switch
        {
            8 => IsValidGtin(digits) || (ExpandUpcE(digits) is { } upcA && IsValidGtin(upcA)),
            12 or 13 => IsValidGtin(digits),
            _ => false,
        };
    }

    public static string ProductSymbology(string digits) => digits.Length switch
    {
        8 => IsValidGtin(digits) ? "EAN-8" : "UPC-E",
        12 => "UPC-A",
        13 => digits.StartsWith("978", StringComparison.Ordinal) || digits.StartsWith("979", StringComparison.Ordinal) ? "ISBN / EAN-13" : "EAN-13",
        _ => "Product code",
    };
}
