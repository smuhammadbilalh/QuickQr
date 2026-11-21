using QuickQr.Core.Content.Internal;
using QuickQr.Core.Security;

namespace QuickQr.Core.Content;

/// <summary>
/// Classifies raw scanned payloads into typed content. Tolerant of case, CRLF/LF line endings and the common
/// generator variants (iOS, Android, ZXing, Docomo). Never throws: anything unrecognised becomes <see cref="TextContent"/>.
/// </summary>
public sealed class ContentParser : IContentParser
{
    public QrContent Parse(string payload)
    {
        if (payload is null) return new TextContent(string.Empty);
        try
        {
            return ParseCore(payload, depth: 0) ?? new TextContent(payload);
        }
#pragma warning disable CA1031 // The contract is "never fails": a malformed payload is shown as text.
        catch (Exception)
#pragma warning restore CA1031
        {
            return new TextContent(payload);
        }
    }

    private static QrContent? ParseCore(string payload, int depth)
    {
        var text = payload.Trim().TrimStart('﻿').Trim();
        if (text.Length == 0) return null;

        if (text.StartsWith(ISecurePayloadProtector.EnvelopePrefix, StringComparison.Ordinal)) return new SecureMessageContent(text);
        if (Starts(text, WifiFormat.Prefix)) return WifiFormat.TryParse(text);
        if (Starts(text, VCardFormat.Begin)) return VCardFormat.TryParse(text);
        if (Starts(text, MeCardFormat.Prefix)) return MeCardFormat.TryParse(text);
        if (Starts(text, CalendarFormat.BeginEvent) || Starts(text, CalendarFormat.BeginCalendar)) return CalendarFormat.TryParse(text);
        if (Starts(text, EmailFormat.MailtoPrefix)) return EmailFormat.TryParseMailto(text);
        if (Starts(text, EmailFormat.MatmsgPrefix)) return EmailFormat.TryParseMatmsg(text);
        if (TelephonyFormat.IsSms(text)) return TelephonyFormat.TryParseSms(text);
        if (Starts(text, TelephonyFormat.TelPrefix)) return TelephonyFormat.TryParseTel(text);
        if (Starts(text, GeoFormat.Prefix)) return GeoFormat.TryParse(text);
        if (CryptoFormat.TryGetCurrency(text, out var currency)) return CryptoFormat.TryParse(currency, text);
        if (EpcFormat.IsEpc(text)) return EpcFormat.TryParse(text);
        if (Starts(text, UpiFormat.Prefix)) return UpiFormat.TryParse(text);
        if (Starts(text, "whatsapp://")) return MessengerFormat.TryParseWhatsAppScheme(text);
        if (Starts(text, "tg://")) return MessengerFormat.TryParseTelegramScheme(text);
        if (AppStoreFormat.IsStoreScheme(text)) return AppStoreFormat.TryParseScheme(text);
        if (Starts(text, "otpauth://") || Starts(text, "otpauth-migration://")) return null; // one-time-password secrets stay plain text

        // Docomo bookmark / legacy URL prefixes: MEBKM:TITLE:..;URL:..;; and URLTO:/URL:
        if (depth == 0 && UnwrapLegacyUrl(text) is { } inner) return ParseCore(inner, depth + 1);

        if (UrlFormat.TryCreateWebUri(text, out var uri, out var normalized))
        {
            return MessengerFormat.TryParseWhatsApp(uri)
                ?? MessengerFormat.TryParseTelegram(uri)
                ?? AppStoreFormat.TryParseUri(uri)
                ?? SocialFormat.TryParse(uri)
                ?? new UrlContent(normalized);
        }

        if (Validators.IsValidProductCode(text)) return new ProductContent(text);
        if (Validators.IsEmail(text)) return new EmailContent(text);
        if (TelephonyFormat.LooksLikePhone(text)) return new PhoneContent(Validators.StripPhone(text));
        return GeoFormat.TryParsePlain(text);
    }

    private static string? UnwrapLegacyUrl(string text)
    {
        if (Starts(text, "MEBKM:"))
        {
            foreach (var (key, raw) in PayloadText.ParseFields(text[6..]))
            {
                if (key == "URL") return PayloadText.Clean(PayloadText.BackslashUnescape(raw));
            }
            return null;
        }
        if (Starts(text, "URLTO:")) return PayloadText.Clean(text[6..]);
        if (Starts(text, "URL:")) return PayloadText.Clean(text[4..]);
        return null;
    }

    private static bool Starts(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
