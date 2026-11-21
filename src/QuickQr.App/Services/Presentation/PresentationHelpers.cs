using System.Globalization;
using QuickQr.Core.Content;
using QuickQr.Core.History;

namespace QuickQr.Services.Presentation;

/// <summary>Material Icons codepoints used by the scan / history / settings screens.</summary>
public static class Glyphs
{
    public const string Star = "\uf09a";        // star
    public const string StarOutline = "\uf09a"; // star_border
    public const string Delete = "\ue92e";      // delete
    public const string Copy = "\ue14d";        // content_copy
    public const string Share = "\ue80d";       // share
    public const string Check = "\ue668";       // check
    public const string Shield = "\uf013";      // verified_user
    public const string Info = "\ue88e";        // info
    public const string Warning = "\uf083";     // warning
    public const string Error = "\uf8b6";       // error
    public const string ScanCamera = "\uf206";  // qr_code_scanner
    public const string QrCode = "\ue00a";      // qr_code_2
    public const string Image = "\ue3f4";       // image

    /// <summary>Content-type icon (delegates to Core's <see cref="IconGlyphs"/>).</summary>
    public static string For(ContentType type) => IconGlyphs.ForContentType(type);
}

public static class ContentTypeNames
{
    public static string For(ContentType type) => type switch
    {
        ContentType.Url => "Link",
        ContentType.Wifi => "Wi-Fi",
        ContentType.Contact => "Contact",
        ContentType.MeCard => "Contact (MeCard)",
        ContentType.Email => "Email",
        ContentType.Sms => "SMS",
        ContentType.Phone => "Phone",
        ContentType.Geo => "Location",
        ContentType.CalendarEvent => "Event",
        ContentType.WhatsApp => "WhatsApp",
        ContentType.Telegram => "Telegram",
        ContentType.CryptoPayment => "Crypto payment",
        ContentType.EpcPayment => "SEPA payment",
        ContentType.UpiPayment => "UPI payment",
        ContentType.SecureMessage => "Encrypted message",
        ContentType.AppStore => "App",
        ContentType.SocialProfile => "Social profile",
        ContentType.Product => "Product",
        _ => "Text",
    };

    public static string For(ScanSource source) => source switch
    {
        ScanSource.Camera => "Camera",
        ScanSource.Image => "Image",
        ScanSource.Clipboard => "Clipboard",
        ScanSource.Batch => "Bulk",
        _ => "QuickQr",
    };
}

public static class RelativeTime
{
    /// <summary>"Just now", "5 min ago", "3 h ago", "Yesterday", "Mon", "12 Mar", "12 Mar 2024".</summary>
    public static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        var local = value.ToLocalTime();
        var localNow = now.ToLocalTime();
        var elapsed = localNow - local;

        if (elapsed < TimeSpan.FromMinutes(1)) return "Just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (local.Date == localNow.Date) return $"{(int)elapsed.TotalHours} h ago";
        if (local.Date == localNow.Date.AddDays(-1)) return "Yesterday";
        if (elapsed < TimeSpan.FromDays(7)) return local.ToString("dddd", CultureInfo.CurrentCulture);
        return local.Year == localNow.Year
            ? local.ToString("d MMM", CultureInfo.CurrentCulture)
            : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}

/// <summary>
/// Splits a host into the part users should check ("example.co.uk") and the rest ("login.secure."), so the
/// registrable domain can be shown in bold. Uses a short list of common multi-label public suffixes.
/// </summary>
public static class RegistrableDomain
{
    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "co.za", "co.in", "net.in", "org.in", "gov.in", "ac.in",
        "co.jp", "ne.jp", "or.jp", "ac.jp", "co.kr", "or.kr",
        "com.br", "com.mx", "com.ar", "com.co", "com.tr", "com.cn", "com.hk", "com.tw", "com.sg", "com.my",
        "com.pk", "org.pk", "edu.pk", "gov.pk", "com.sa", "com.eg", "com.ng", "co.id", "or.id", "co.il", "co.th",
        "github.io", "blogspot.com", "azurewebsites.net", "herokuapp.com", "vercel.app", "netlify.app", "pages.dev", "web.app",
    };

    public static (string Prefix, string Registrable) Split(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return (string.Empty, string.Empty);
        var h = host.Trim().TrimEnd('.');

        // IP literals and single labels are shown whole.
        if (h.StartsWith('[') || System.Net.IPAddress.TryParse(h, out _) || !h.Contains('.', StringComparison.Ordinal))
            return (string.Empty, h);

        var labels = h.Split('.');
        var take = 2;
        if (labels.Length >= 3 && MultiLabelSuffixes.Contains(labels[^2] + "." + labels[^1])) take = 3;
        if (labels.Length <= take) return (string.Empty, h);

        var registrable = string.Join('.', labels[^take..]);
        return (h[..(h.Length - registrable.Length)], registrable);
    }
}
