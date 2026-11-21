using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Web links: normalisation, validation and URI helpers shared by link-based formats.</summary>
internal static partial class UrlFormat
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*://", RegexOptions.CultureInvariant)]
    private static partial Regex SchemeWithAuthority();

    /// <summary>Trims, adds https:// when there is no scheme and encodes spaces.</summary>
    public static string Normalize(string? url)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (trimmed.Length == 0) return trimmed;
        if (!SchemeWithAuthority().IsMatch(trimmed)) trimmed = "https://" + trimmed;
        return trimmed.Replace(" ", "%20", StringComparison.Ordinal);
    }

    public static Error? Validate(UrlContent content)
    {
        var normalized = Normalize(content.Url);
        if (normalized.Length == 0) return Error.Validation("Enter a web address.");
        if (normalized.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return Error.Validation("A web address can't contain line breaks or tabs.");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return Error.Validation("Enter a valid web address, e.g. https://example.com.");
        return null;
    }

    /// <summary>Recognises http(s) links and scheme-less "www." / known short hosts. Links with whitespace are not URLs.</summary>
    public static bool TryCreateWebUri(string text, out Uri uri, out string normalized)
    {
        uri = null!;
        normalized = text;
        if (text.Any(char.IsWhiteSpace)) return false;

        string candidate;
        if (PayloadText.StartsWithIgnoreCase(text, "http://") || PayloadText.StartsWithIgnoreCase(text, "https://"))
            candidate = text;
        else if (PayloadText.StartsWithIgnoreCase(text, "www.") || PayloadText.StartsWithIgnoreCase(text, "wa.me/") || PayloadText.StartsWithIgnoreCase(text, "t.me/"))
            candidate = "https://" + text;
        else
            return false;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var created) ||
            created.Scheme is not ("http" or "https") || string.IsNullOrEmpty(created.Host))
            return false;

        uri = created;
        normalized = candidate;
        return true;
    }

    /// <summary>Lower-cases and strips "www.", "m." and "mobile." prefixes.</summary>
    public static string NormalizeHost(string host)
    {
        var h = host.ToLowerInvariant().TrimEnd('.');
        foreach (var prefix in new[] { "www.", "m.", "mobile." })
        {
            if (h.StartsWith(prefix, StringComparison.Ordinal)) return h[prefix.Length..];
        }
        return h;
    }

    /// <summary>Decoded, non-empty path segments.</summary>
    public static string[] PathSegments(Uri uri) =>
        [.. uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => PayloadText.PercentDecode(s))];

    /// <summary>The host of a URL for display, or null when it can't be parsed.</summary>
    public static string? DisplayHost(string url)
    {
        var normalized = Normalize(url);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
