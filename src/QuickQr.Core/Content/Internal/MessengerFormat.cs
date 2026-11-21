using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>WhatsApp click-to-chat (<c>https://wa.me/</c>) and Telegram (<c>https://t.me/</c>) links.</summary>
internal static partial class MessengerFormat
{
    [GeneratedRegex(@"^[A-Za-z0-9_]{4,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramUsername();

    private static readonly FrozenSet<string> TelegramReserved = new[]
    {
        "joinchat", "addstickers", "addemoji", "addtheme", "share", "proxy", "socks", "iv", "s", "c", "login", "setlanguage", "confirmphone", "invoice", "boost",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // ---- WhatsApp ----

    public static string WriteWhatsApp(WhatsAppContent content)
    {
        var digits = Validators.DigitsOnly(content.Number);
        var url = "https://wa.me/" + digits;
        return PayloadText.Clean(content.Message) is null ? url : url + "?text=" + PayloadText.PercentEncode(content.Message!);
    }

    public static Error? ValidateWhatsApp(WhatsAppContent content)
    {
        var hasMessage = PayloadText.Clean(content.Message) is not null;
        if (string.IsNullOrWhiteSpace(content.Number))
            return hasMessage ? null : Error.Validation("Enter the WhatsApp phone number.");
        var normalized = Validators.NormalizePhone(content.Number);
        var digits = normalized is null ? 0 : Validators.CountDigits(normalized);
        return digits is >= 7 and <= 15
            ? null
            : Error.Validation("Enter the full international number with country code, e.g. 15551234567.");
    }

    public static QrContent? TryParseWhatsApp(Uri uri)
    {
        var host = UrlFormat.NormalizeHost(uri.Host);
        var segments = UrlFormat.PathSegments(uri);
        var query = PayloadText.ParseQuery(uri.Query, plusAsSpace: true);
        var text = PayloadText.GetValue(query, "text");

        if (host == "wa.me")
        {
            if (segments.Length > 1) return null; // wa.me/message/XYZ, wa.me/c/...
            var number = segments.Length == 1 ? Validators.DigitsOnly(segments[0]) : string.Empty;
            if (segments.Length == 1 && number.Length == 0) return null;
            return number.Length == 0 && text is null ? null : new WhatsAppContent(number, EmptyToNull(text));
        }

        if (host is "api.whatsapp.com" or "whatsapp.com" && segments is ["send"] or ["send", ""])
        {
            var number = Validators.DigitsOnly(PayloadText.GetValue(query, "phone"));
            return number.Length == 0 && text is null ? null : new WhatsAppContent(number, EmptyToNull(text));
        }

        return null;
    }

    /// <summary>whatsapp://send?phone=..&amp;text=..</summary>
    public static QrContent? TryParseWhatsAppScheme(string payload)
    {
        var questionMark = payload.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0) return null;
        var query = PayloadText.ParseQuery(payload[(questionMark + 1)..], plusAsSpace: true);
        var number = Validators.DigitsOnly(PayloadText.GetValue(query, "phone"));
        var text = PayloadText.GetValue(query, "text");
        return number.Length == 0 && text is null ? null : new WhatsAppContent(number, EmptyToNull(text));
    }

    // ---- Telegram ----

    public static string NormalizeTelegram(string? username)
    {
        var value = (username ?? string.Empty).Trim();
        foreach (var prefix in new[] { "https://", "http://" })
        {
            if (PayloadText.StartsWithIgnoreCase(value, prefix)) value = value[prefix.Length..];
        }
        foreach (var host in new[] { "www.t.me/", "t.me/", "telegram.me/", "telegram.dog/" })
        {
            if (PayloadText.StartsWithIgnoreCase(value, host))
            {
                value = value[host.Length..];
                break;
            }
        }
        var end = value.IndexOfAny(['/', '?', '#']);
        if (end >= 0) value = value[..end];
        return value.TrimStart('@');
    }

    public static string WriteTelegram(TelegramContent content) => "https://t.me/" + NormalizeTelegram(content.Username);

    public static Error? ValidateTelegram(TelegramContent content)
    {
        var username = NormalizeTelegram(content.Username);
        if (username.Length == 0) return Error.Validation("Enter the Telegram username.");
        return TelegramUsername().IsMatch(username) && !TelegramReserved.Contains(username)
            ? null
            : Error.Validation("Telegram usernames are 4 to 32 letters, digits or underscores.");
    }

    public static QrContent? TryParseTelegram(Uri uri)
    {
        var host = UrlFormat.NormalizeHost(uri.Host);
        if (host is not ("t.me" or "telegram.me" or "telegram.dog")) return null;
        var segments = UrlFormat.PathSegments(uri);
        if (segments.Length != 1) return null;
        var username = segments[0].TrimStart('@');
        return TelegramUsername().IsMatch(username) && !TelegramReserved.Contains(username) ? new TelegramContent(username) : null;
    }

    /// <summary>tg://resolve?domain=username</summary>
    public static QrContent? TryParseTelegramScheme(string payload)
    {
        var questionMark = payload.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0) return null;
        var query = PayloadText.ParseQuery(payload[(questionMark + 1)..], plusAsSpace: false);
        var domain = PayloadText.GetValue(query, "domain");
        return domain is not null && TelegramUsername().IsMatch(domain) ? new TelegramContent(domain) : null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
