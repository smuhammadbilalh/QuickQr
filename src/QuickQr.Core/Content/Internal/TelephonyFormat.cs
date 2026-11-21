namespace QuickQr.Core.Content.Internal;

/// <summary>SMS (<c>SMSTO:</c>, RFC 5724 <c>sms:</c>) and phone (RFC 3966 <c>tel:</c>) payloads.</summary>
internal static class TelephonyFormat
{
    public const string SmsToPrefix = "SMSTO:";
    public const string TelPrefix = "tel:";

    public static string WriteSms(SmsContent content)
    {
        var number = Validators.StripPhone(content.Number);
        return string.IsNullOrEmpty(content.Message) ? SmsToPrefix + number : $"{SmsToPrefix}{number}:{content.Message}";
    }

    public static string WritePhone(PhoneContent content) => TelPrefix + Validators.StripPhone(content.Number);

    public static Error? ValidateNumber(string? number) =>
        string.IsNullOrWhiteSpace(number) ? Error.Validation("Enter a phone number.")
        : Validators.NormalizePhone(number) is null ? Error.Validation($"“{number.Trim()}” isn't a valid phone number.")
        : null;

    public static bool IsSms(string payload) =>
        PayloadText.StartsWithIgnoreCase(payload, "SMSTO:") || PayloadText.StartsWithIgnoreCase(payload, "MMSTO:") ||
        PayloadText.StartsWithIgnoreCase(payload, "sms:") || PayloadText.StartsWithIgnoreCase(payload, "mms:");

    public static QrContent? TryParseSms(string payload)
    {
        string numberPart;
        string? message;

        if (PayloadText.StartsWithIgnoreCase(payload, "SMSTO:") || PayloadText.StartsWithIgnoreCase(payload, "MMSTO:"))
        {
            // SMSTO:number:message — the message may itself contain colons.
            var rest = payload[6..];
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            numberPart = colon < 0 ? rest : rest[..colon];
            message = colon < 0 ? null : rest[(colon + 1)..];
        }
        else
        {
            // sms:number?body=text (RFC 5724), sms:number&body=text (iOS), sms:number;body=text, sms:number:text.
            var rest = payload[4..];
            if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];
            var separator = rest.IndexOfAny(['?', '&', ';']);
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            if (separator >= 0 && (colon < 0 || separator < colon))
            {
                numberPart = rest[..separator];
                var query = PayloadText.ParseQuery(rest[(separator + 1)..].Replace(';', '&'), plusAsSpace: false);
                message = PayloadText.GetValue(query, "body");
            }
            else
            {
                numberPart = colon < 0 ? rest : rest[..colon];
                message = colon < 0 ? null : rest[(colon + 1)..];
            }
            numberPart = PayloadText.PercentDecode(numberPart);
        }

        // Several recipients ("sms:+1,+2"): keep the first.
        var firstNumber = numberPart.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var number = Validators.StripPhone(firstNumber);
        if (string.IsNullOrEmpty(message)) message = null;
        return number.Length == 0 && message is null ? null : new SmsContent(number, message);
    }

    public static QrContent? TryParseTel(string payload)
    {
        var rest = payload[TelPrefix.Length..];
        if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];
        var parameters = rest.IndexOfAny([';', '?']);
        if (parameters >= 0) rest = rest[..parameters];
        var number = Validators.StripPhone(PayloadText.PercentDecode(rest));
        return Validators.CountDigits(number) == 0 ? null : new PhoneContent(number);
    }

    /// <summary>Bare international numbers such as "+44 20 7946 0958".</summary>
    public static bool LooksLikePhone(string text)
    {
        if (text.Length < 8 || text.Length > 32 || text[0] != '+') return false;
        foreach (var c in text.AsSpan(1))
        {
            if (!char.IsAsciiDigit(c) && c is not (' ' or '-' or '.' or '(' or ')')) return false;
        }
        return Validators.CountDigits(text) is >= 7 and <= 15;
    }
}
