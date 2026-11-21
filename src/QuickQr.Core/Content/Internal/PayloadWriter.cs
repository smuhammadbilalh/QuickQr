namespace QuickQr.Core.Content.Internal;

/// <summary>
/// Best-effort serialisation without validation. Used for action targets of scanned content,
/// which may not meet the encoder's stricter rules (e.g. a 7-character Wi-Fi password).
/// </summary>
internal static class PayloadWriter
{
    public static string Write(QrContent content) => content switch
    {
        TextContent c => c.Text ?? string.Empty,
        UrlContent c => UrlFormat.Normalize(c.Url),
        WifiContent c => WifiFormat.Write(c),
        ContactContent c => VCardFormat.Write(c),
        MeCardContent c => MeCardFormat.Write(c),
        EmailContent c => EmailFormat.Write(c),
        SmsContent c => TelephonyFormat.WriteSms(c),
        PhoneContent c => TelephonyFormat.WritePhone(c),
        GeoContent c => GeoFormat.Write(c),
        CalendarEventContent c => CalendarFormat.Write(c),
        WhatsAppContent c => MessengerFormat.WriteWhatsApp(c),
        TelegramContent c => MessengerFormat.WriteTelegram(c),
        CryptoPaymentContent c => CryptoFormat.Write(c),
        EpcPaymentContent c => EpcFormat.Write(c),
        UpiPaymentContent c => UpiFormat.Write(c),
        SecureMessageContent c => (c.Envelope ?? string.Empty).Trim(),
        AppStoreContent c => AppStoreFormat.Write(c),
        SocialProfileContent c => SocialFormat.Write(c),
        ProductContent c => ProductCode(c.Code),
        _ => string.Empty,
    };

    public static string ProductCode(string? code) =>
        (code ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
}
