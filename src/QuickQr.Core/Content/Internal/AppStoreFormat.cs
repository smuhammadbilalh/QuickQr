using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Store links for Apple App Store, Google Play and Microsoft Store.</summary>
internal static partial class AppStoreFormat
{
    [GeneratedRegex(@"(?:^|/)id(\d{1,15})(?:$|[/?#])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AppleIdInPath();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageName();

    [GeneratedRegex(@"^[A-Za-z0-9]{9,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex MicrosoftId();

    public static string DisplayName(AppStoreKind store) => store switch
    {
        AppStoreKind.GooglePlay => "Google Play",
        AppStoreKind.MicrosoftStore => "Microsoft Store",
        _ => "App Store",
    };

    /// <summary>Extracts the store id from an id or a pasted store link.</summary>
    public static string NormalizeId(AppStoreKind store, string? appId)
    {
        var value = (appId ?? string.Empty).Trim();
        if (value.Length == 0) return value;

        if (value.Contains("://", StringComparison.Ordinal) || value.Contains(".com/", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && TryParseUri(uri) is AppStoreContent parsed && parsed.Store == store)
                return parsed.AppId;
        }

        return store switch
        {
            AppStoreKind.AppleAppStore => value.StartsWith("id", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            AppStoreKind.MicrosoftStore => value.ToUpperInvariant(),
            _ => value,
        };
    }

    public static string Write(AppStoreContent content)
    {
        var id = NormalizeId(content.Store, content.AppId);
        return content.Store switch
        {
            AppStoreKind.GooglePlay => "https://play.google.com/store/apps/details?id=" + PayloadText.PercentEncode(id),
            AppStoreKind.MicrosoftStore => "https://apps.microsoft.com/detail/" + PayloadText.PercentEncode(id),
            _ => "https://apps.apple.com/app/id" + Validators.DigitsOnly(id),
        };
    }

    public static Error? Validate(AppStoreContent content)
    {
        var id = NormalizeId(content.Store, content.AppId);
        if (id.Length == 0) return Error.Validation($"Enter the {DisplayName(content.Store)} app id.");
        return content.Store switch
        {
            AppStoreKind.AppleAppStore when !(Validators.IsAllDigits(id) && id.Length <= 15) =>
                Error.Validation("Enter the numeric App Store id, e.g. 284882215."),
            AppStoreKind.GooglePlay when !PackageName().IsMatch(id) =>
                Error.Validation("Enter the Google Play package name, e.g. com.example.app."),
            AppStoreKind.MicrosoftStore when !MicrosoftId().IsMatch(id) =>
                Error.Validation("Enter the Microsoft Store product id, e.g. 9NBLGGH4NNS1."),
            AppStoreKind.AppleAppStore or AppStoreKind.GooglePlay or AppStoreKind.MicrosoftStore => null,
            _ => Error.Validation("Choose a supported app store."),
        };
    }

    public static QrContent? TryParseUri(Uri uri)
    {
        var host = UrlFormat.NormalizeHost(uri.Host);
        var segments = UrlFormat.PathSegments(uri);
        var query = PayloadText.ParseQuery(uri.Query, plusAsSpace: true);

        switch (host)
        {
            case "apps.apple.com" or "itunes.apple.com":
                return AppleIdInPath().Match(uri.AbsolutePath) is { Success: true } apple
                    ? new AppStoreContent(AppStoreKind.AppleAppStore, apple.Groups[1].Value)
                    : null;

            case "play.google.com" when segments is ["store", "apps", "details", ..]:
                return PayloadText.Clean(PayloadText.GetValue(query, "id")) is { } package && PackageName().IsMatch(package)
                    ? new AppStoreContent(AppStoreKind.GooglePlay, package)
                    : null;

            case "apps.microsoft.com" or "microsoft.com":
                if (host == "microsoft.com" && !segments.Any(s => s.Equals("store", StringComparison.OrdinalIgnoreCase) || s.Equals("p", StringComparison.OrdinalIgnoreCase)))
                    return null;
                var id = segments.Length > 0 ? segments[^1] : null;
                if (id is null || !MicrosoftId().IsMatch(id) || !id.Any(char.IsAsciiDigit)) return null;
                return new AppStoreContent(AppStoreKind.MicrosoftStore, id.ToUpperInvariant());
        }
        return null;
    }

    /// <summary>market://details?id=…, itms-apps://…/id123, ms-windows-store://pdp/?productid=…</summary>
    public static QrContent? TryParseScheme(string payload)
    {
        var questionMark = payload.IndexOf('?', StringComparison.Ordinal);
        var query = questionMark < 0 ? [] : PayloadText.ParseQuery(payload[(questionMark + 1)..], plusAsSpace: true);

        if (PayloadText.StartsWithIgnoreCase(payload, "market://"))
        {
            return PayloadText.Clean(PayloadText.GetValue(query, "id")) is { } package && PackageName().IsMatch(package)
                ? new AppStoreContent(AppStoreKind.GooglePlay, package)
                : null;
        }

        if (PayloadText.StartsWithIgnoreCase(payload, "itms-apps://") || PayloadText.StartsWithIgnoreCase(payload, "itms-appss://"))
        {
            return AppleIdInPath().Match(questionMark < 0 ? payload : payload[..questionMark]) is { Success: true } apple
                ? new AppStoreContent(AppStoreKind.AppleAppStore, apple.Groups[1].Value)
                : null;
        }

        if (PayloadText.StartsWithIgnoreCase(payload, "ms-windows-store://"))
        {
            return PayloadText.Clean(PayloadText.GetValue(query, "productid")) is { } id && MicrosoftId().IsMatch(id)
                ? new AppStoreContent(AppStoreKind.MicrosoftStore, id.ToUpperInvariant())
                : null;
        }

        return null;
    }

    public static bool IsStoreScheme(string payload) =>
        PayloadText.StartsWithIgnoreCase(payload, "market://") || PayloadText.StartsWithIgnoreCase(payload, "itms-apps://") ||
        PayloadText.StartsWithIgnoreCase(payload, "itms-appss://") || PayloadText.StartsWithIgnoreCase(payload, "ms-windows-store://");
}
