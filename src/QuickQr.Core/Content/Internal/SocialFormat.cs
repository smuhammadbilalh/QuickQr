using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Canonical social-network profile links and their recognition.</summary>
internal static partial class SocialFormat
{
    [GeneratedRegex(@"^[\p{L}\p{N}._\-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandleRegex();

    [GeneratedRegex(@"^profile\.php\?id=\d{1,30}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FacebookProfileId();

    private static readonly FrozenDictionary<string, SocialNetwork> Hosts = new Dictionary<string, SocialNetwork>(StringComparer.Ordinal)
    {
        ["instagram.com"] = SocialNetwork.Instagram,
        ["instagr.am"] = SocialNetwork.Instagram,
        ["facebook.com"] = SocialNetwork.Facebook,
        ["fb.com"] = SocialNetwork.Facebook,
        ["x.com"] = SocialNetwork.X,
        ["twitter.com"] = SocialNetwork.X,
        ["linkedin.com"] = SocialNetwork.LinkedIn,
        ["tiktok.com"] = SocialNetwork.TikTok,
        ["youtube.com"] = SocialNetwork.YouTube,
        ["github.com"] = SocialNetwork.GitHub,
        ["snapchat.com"] = SocialNetwork.Snapchat,
        ["pinterest.com"] = SocialNetwork.Pinterest,
        ["threads.net"] = SocialNetwork.Threads,
        ["threads.com"] = SocialNetwork.Threads,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // First path segments that are site features rather than profiles.
    private static readonly FrozenSet<string> Reserved = new[]
    {
        "about", "accounts", "explore", "p", "reel", "reels", "stories", "tv", "direct", "legal", "privacy", "terms", "help",
        "home", "search", "i", "intent", "share", "sharer", "sharer.php", "hashtag", "settings", "notifications", "messages", "login",
        "signup", "groups", "events", "watch", "pages", "photo.php", "story.php", "marketplace", "gaming", "features", "pricing",
        "topics", "sponsors", "orgs", "enterprise", "pin", "ideas", "today", "business", "dialog", "plugins", "policies",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string DisplayName(SocialNetwork network) => network switch
    {
        SocialNetwork.Instagram => "Instagram",
        SocialNetwork.Facebook => "Facebook",
        SocialNetwork.X => "X",
        SocialNetwork.LinkedIn => "LinkedIn",
        SocialNetwork.TikTok => "TikTok",
        SocialNetwork.YouTube => "YouTube",
        SocialNetwork.GitHub => "GitHub",
        SocialNetwork.Snapchat => "Snapchat",
        SocialNetwork.Pinterest => "Pinterest",
        SocialNetwork.Threads => "Threads",
        _ => "Profile",
    };

    /// <summary>Accepts a bare handle, "@handle" or a pasted profile link and returns the handle.</summary>
    public static string NormalizeHandle(SocialNetwork network, string? handle)
    {
        var value = (handle ?? string.Empty).Trim();
        if (value.Length == 0) return value;

        if (LooksLikeUrl(value))
        {
            var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && TryParse(uri) is SocialProfileContent parsed && parsed.Network == network)
                return parsed.Handle;
        }

        value = value.TrimStart('@').TrimEnd('/');
        if (network == SocialNetwork.LinkedIn && value.StartsWith("in/", StringComparison.OrdinalIgnoreCase)) value = value[3..];
        return value;
    }

    private static bool LooksLikeUrl(string value)
    {
        if (value.Contains("://", StringComparison.Ordinal)) return true;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0) return false;
        var host = UrlFormat.NormalizeHost(value[..slash]);
        return Hosts.ContainsKey(host);
    }

    private static bool HasPrefix(string handle, params string[] prefixes) =>
        prefixes.Any(p => handle.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static string Write(SocialProfileContent content)
    {
        var h = NormalizeHandle(content.Network, content.Handle);
        return content.Network switch
        {
            SocialNetwork.Instagram => "https://instagram.com/" + h,
            SocialNetwork.Facebook => "https://facebook.com/" + h,
            SocialNetwork.X => "https://x.com/" + h,
            SocialNetwork.LinkedIn => HasPrefix(h, "company/", "school/", "showcase/") ? "https://linkedin.com/" + h : "https://linkedin.com/in/" + h,
            SocialNetwork.TikTok => "https://tiktok.com/@" + h,
            SocialNetwork.YouTube => HasPrefix(h, "channel/", "c/", "user/") ? "https://youtube.com/" + h : "https://youtube.com/@" + h,
            SocialNetwork.GitHub => "https://github.com/" + h,
            SocialNetwork.Snapchat => "https://snapchat.com/add/" + h,
            SocialNetwork.Pinterest => "https://pinterest.com/" + h,
            SocialNetwork.Threads => "https://threads.net/@" + h,
            _ => h,
        };
    }

    public static Error? Validate(SocialProfileContent content)
    {
        if (!Enum.IsDefined(content.Network)) return Error.Validation("Choose a supported social network.");
        var name = DisplayName(content.Network);
        var h = NormalizeHandle(content.Network, content.Handle);
        if (h.Length == 0) return Error.Validation($"Enter the {name} username.");
        return IsValidHandle(content.Network, h) ? null : Error.Validation($"That doesn't look like a valid {name} username.");
    }

    private static bool IsValidHandle(SocialNetwork network, string handle)
    {
        var slash = handle.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return HandleRegex().IsMatch(handle) ||
                   (network == SocialNetwork.Facebook && FacebookProfileId().IsMatch(handle));
        }

        var prefix = handle[..(slash + 1)];
        var rest = handle[(slash + 1)..];
        var allowedPrefix = network switch
        {
            SocialNetwork.LinkedIn => HasPrefix(prefix, "company/", "school/", "showcase/"),
            SocialNetwork.YouTube => HasPrefix(prefix, "channel/", "c/", "user/"),
            _ => false,
        };
        return allowedPrefix && HandleRegex().IsMatch(rest);
    }

    public static QrContent? TryParse(Uri uri)
    {
        var host = UrlFormat.NormalizeHost(uri.Host);
        if (!Hosts.TryGetValue(host, out var network)) return null;
        var s = UrlFormat.PathSegments(uri);

        string? handle = network switch
        {
            SocialNetwork.Instagram or SocialNetwork.X or SocialNetwork.GitHub or SocialNetwork.Pinterest =>
                s is [var only] && !only.StartsWith('@') ? only : null,
            SocialNetwork.Facebook => s switch
            {
                ["profile.php"] when PayloadText.GetValue(PayloadText.ParseQuery(uri.Query, false), "id") is { } id && Validators.IsAllDigits(id) => "profile.php?id=" + id,
                [var only] when only != "profile.php" => only,
                _ => null,
            },
            SocialNetwork.LinkedIn => s switch
            {
                ["in", var h] => h,
                [var kind, var h] when HasPrefix(kind + "/", "company/", "school/", "showcase/") => kind.ToLowerInvariant() + "/" + h,
                _ => null,
            },
            SocialNetwork.YouTube => s switch
            {
                [var only] when only.StartsWith('@') => only[1..],
                [var kind, var h] when HasPrefix(kind + "/", "channel/", "c/", "user/") => kind.ToLowerInvariant() + "/" + h,
                _ => null,
            },
            SocialNetwork.TikTok or SocialNetwork.Threads => s is [var only] && only.StartsWith('@') ? only[1..] : null,
            SocialNetwork.Snapchat => s is ["add", var h] ? h : null,
            _ => null,
        };

        if (handle is null || Reserved.Contains(handle)) return null;
        return IsValidHandle(network, handle) ? new SocialProfileContent(network, handle) : null;
    }
}
