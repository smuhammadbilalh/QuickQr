using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickQr.Core.Security.Internal;

namespace QuickQr.Core.Security;

/// <summary>
/// Offline heuristics against QR phishing ("quishing"). Pure and fast: no network, static frozen lookup tables.
/// Signal codes are stable and can be used for analytics or localisation:
/// invalid_url, dangerous_scheme, unusual_scheme, insecure_scheme, credentials_in_url, ip_literal_host, punycode_host,
/// mixed_script_host, excessive_subdomains, url_shortener, suspicious_tld, brand_impersonation, non_standard_port,
/// very_long_url, obfuscated_encoding, file_download.
/// </summary>
public sealed partial class UrlSafetyAnalyzer : IUrlSafetyAnalyzer
{
    public const string InvalidUrl = "invalid_url";
    public const string DangerousScheme = "dangerous_scheme";
    public const string UnusualScheme = "unusual_scheme";
    public const string InsecureScheme = "insecure_scheme";
    public const string CredentialsInUrl = "credentials_in_url";
    public const string IpLiteralHost = "ip_literal_host";
    public const string PunycodeHost = "punycode_host";
    public const string MixedScriptHost = "mixed_script_host";
    public const string ExcessiveSubdomains = "excessive_subdomains";
    public const string UrlShortener = "url_shortener";
    public const string SuspiciousTld = "suspicious_tld";
    public const string BrandImpersonation = "brand_impersonation";
    public const string NonStandardPort = "non_standard_port";
    public const string VeryLongUrl = "very_long_url";
    public const string ObfuscatedEncoding = "obfuscated_encoding";
    public const string FileDownload = "file_download";

    private const int LongUrlLength = 200;
    private const int MaxHostLabels = 4;

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9+.\-]*):", RegexOptions.CultureInvariant)]
    private static partial Regex SchemeRegex();

    [GeneratedRegex(@"%(?:3[0-9]|4[1-9A-F]|5[0-9A]|6[1-9A-F]|7[0-9A]|25[0-9A-F]{2})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ObfuscatedEscape();

    [GeneratedRegex(@"^(?:0x[0-9a-f]*|\d+)(?:\.(?:0x[0-9a-f]*|\d+)){0,3}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumericHost();

    [GeneratedRegex(@"^(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedQuad();

    public UrlSafetyReport Analyze(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var signals = new List<SafetySignal>();
        var trimmed = url.Trim();

        // Browsers ignore tabs and line breaks inside URLs, so attackers use them to hide schemes ("java\tscript:").
        var compact = RemoveControlCharacters(trimmed);
        if (compact.Length == 0)
            return Report(url, signals, null, Signal(InvalidUrl, "This isn't a valid web address.", RiskLevel.High));

        var schemeMatch = SchemeRegex().Match(compact);
        var scheme = schemeMatch.Success ? schemeMatch.Groups[1].Value.ToLowerInvariant() : null;
        if (scheme is not null && SafetyLists.DangerousSchemes.Contains(scheme))
        {
            return Report(url, signals, null, Signal(DangerousScheme,
                $"This link runs code or opens local data (“{scheme}:”) instead of a website.", RiskLevel.High));
        }

        var hasAuthority = compact.Contains("://", StringComparison.Ordinal) && scheme is not null &&
                           compact.AsSpan(scheme.Length).StartsWith("://");
        var looksLikeHostPort = scheme is not null && !hasAuthority && compact.Length > scheme.Length + 1 && char.IsAsciiDigit(compact[scheme.Length + 1]);
        var candidate = scheme is null || looksLikeHostPort ? "https://" + compact : compact;

        // Browsers decode a percent-encoded host ("%65xample.com" opens example.com); System.Uri rejects it.
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !TryParseWithDecodedHost(candidate, out uri))
            return Report(url, signals, null, Signal(InvalidUrl, "This isn't a valid web address.", RiskLevel.High));

        if (uri.Scheme is not ("http" or "https"))
        {
            // Not a web page (mailto:, market:, otpauth:, ...): the host heuristics below don't apply.
            signals.Add(Signal(UnusualScheme, $"This link opens another app (“{uri.Scheme}:”) rather than a website.", RiskLevel.Low));
            return Report(url, signals, string.IsNullOrEmpty(uri.Host) ? null : ToUnicode(SafeIdnHost(uri)));
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return Report(url, signals, null, Signal(InvalidUrl, "This web address has no website name.", RiskLevel.High));
        }

        if (uri.Scheme == "http")
            signals.Add(Signal(InsecureScheme, "The connection isn't encrypted (http). Others on the network could see or change the page.", RiskLevel.Medium));

        var asciiHost = SafeIdnHost(uri);
        var unicodeHost = ToUnicode(asciiHost);
        var displayHost = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : unicodeHost;

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            signals.Add(Signal(CredentialsInUrl,
                $"The link hides its real destination ({displayHost}) behind “{uri.UserInfo}@”.", RiskLevel.High));
        }

        var rawHost = RawHost(candidate);
        var isNumericHost = NumericHost().IsMatch(rawHost);
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || isNumericHost)
        {
            signals.Add(Signal(IpLiteralHost, "The link points to a raw IP address instead of a named website.", RiskLevel.Medium));
            if (isNumericHost && !DottedQuad().IsMatch(rawHost))
                signals.Add(Signal(ObfuscatedEncoding, "The IP address is written in an unusual (hexadecimal or integer) form to disguise it.", RiskLevel.Low));
        }
        else
        {
            AnalyzeHost(asciiHost, unicodeHost, signals);
        }

        if (!uri.IsDefaultPort && uri.Port > 0)
            signals.Add(Signal(NonStandardPort, $"The link uses an unusual port ({uri.Port.ToString(CultureInfo.InvariantCulture)}).", RiskLevel.Low));

        if (trimmed.Length > LongUrlLength)
            signals.Add(Signal(VeryLongUrl, "The link is unusually long, which can hide where it really goes.", RiskLevel.Low));

        if (!signals.Any(s => s.Code == ObfuscatedEncoding) && (ObfuscatedEscape().IsMatch(compact) || rawHost.Contains('%', StringComparison.Ordinal)))
            signals.Add(Signal(ObfuscatedEncoding, "Parts of the link are encoded to disguise them.", RiskLevel.Low));

        var extension = FileExtension(uri.AbsolutePath);
        if (extension is not null && SafetyLists.DownloadExtensions.Contains(extension))
            signals.Add(Signal(FileDownload, $"The link downloads a .{extension} file. Only install apps from official stores.", RiskLevel.Medium));

        return Report(url, signals, displayHost);
    }

    private static void AnalyzeHost(string asciiHost, string unicodeHost, List<SafetySignal> signals)
    {
        var labels = asciiHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0) return;
        var unicodeLabels = unicodeHost.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (labels.Any(l => l.StartsWith("xn--", StringComparison.Ordinal)))
        {
            signals.Add(Signal(PunycodeHost,
                $"The address uses international characters ({unicodeHost}). Check that every letter is what you expect.", RiskLevel.Medium));
        }

        if (HasMixedScripts(unicodeLabels))
        {
            signals.Add(Signal(MixedScriptHost,
                $"The address mixes alphabets that look alike ({unicodeHost}), a common trick to imitate a real site.", RiskLevel.High));
        }

        if (labels.Length > MaxHostLabels)
            signals.Add(Signal(ExcessiveSubdomains, "The address has an unusual number of subdomains.", RiskLevel.Low));

        var registrable = RegistrableDomain(labels);
        if (SafetyLists.Shorteners.Contains(registrable) || SafetyLists.Shorteners.Contains(asciiHost))
            signals.Add(Signal(UrlShortener, "This is a short link: the final destination is hidden until you open it.", RiskLevel.Low));

        var tld = labels[^1];
        if (SafetyLists.HighRiskTlds.Contains(tld))
            signals.Add(Signal(SuspiciousTld, $"The “.{tld}” domain ending is frequently used for scams.", RiskLevel.Medium));
        else if (SafetyLists.SuspiciousTlds.Contains(tld))
            signals.Add(Signal(SuspiciousTld, $"The “.{tld}” domain ending is often used for scams.", RiskLevel.Low));

        if (FindImpersonatedBrand(unicodeHost, labels, registrable) is { } brand)
        {
            signals.Add(Signal(BrandImpersonation,
                $"The address mentions “{brand}” but the site is actually {ToUnicode(registrable)}.", RiskLevel.High));
        }
        else if (MentionsBankDeceptively(labels, registrable))
        {
            signals.Add(Signal(BrandImpersonation,
                $"The address mentions a bank but the site is actually {ToUnicode(registrable)}.", RiskLevel.Medium));
        }
    }

    // ---- Brand impersonation ----

    private static string? FindImpersonatedBrand(string unicodeHost, string[] asciiLabels, string registrable)
    {
        var skeleton = ToSkeleton(unicodeHost);
        var tokens = skeleton.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var (brand, owned) in SafetyLists.Brands)
        {
            if (!tokens.Any(t => MatchesBrand(t, brand) || MatchesBrand(Deleet(t), brand))) continue;
            if (IsOwnedBy(brand, owned, asciiLabels, registrable)) continue;
            return brand;
        }
        return null;
    }

    private static bool MatchesBrand(string token, string brand)
    {
        if (token == brand) return true;
        if (brand.Length >= 7 && token.Contains(brand, StringComparison.Ordinal)) return true;
        if (token.StartsWith(brand, StringComparison.Ordinal) && IsAffix(token[brand.Length..])) return true;
        return token.EndsWith(brand, StringComparison.Ordinal) && IsAffix(token[..^brand.Length]);
    }

    private static bool IsAffix(string value) =>
        value.Length > 0 && (value.All(char.IsAsciiDigit) || SafetyLists.PhishingAffixes.Contains(value));

    private static bool IsOwnedBy(string brand, IReadOnlySet<string> owned, string[] labels, string registrable)
    {
        if (owned.Contains(registrable)) return true;
        if (labels[^1] == brand) return true; // brand TLD, e.g. ".google"

        // brand.<country> / brand.co.<country> / brand.com|net|org — but not brand.zip or brand.xyz.
        var dot = registrable.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || registrable[..dot] != brand) return false;
        var suffix = registrable[(dot + 1)..];
        if (SafetyLists.HighRiskTlds.Contains(labels[^1]) || SafetyLists.SuspiciousTlds.Contains(labels[^1])) return false;
        return suffix.Length == 2 || suffix is "com" or "net" or "org" || SafetyLists.MultiPartSuffixes.Contains(suffix);
    }

    private static bool MentionsBankDeceptively(string[] labels, string registrable)
    {
        var registrableLabels = registrable.Count(c => c == '.') + 1;
        var subdomains = labels.Take(Math.Max(0, labels.Length - registrableLabels)).ToArray();
        if (subdomains.Length == 0) return false;
        if (SafetyLists.FinancialKeywords.Any(k => registrable.Contains(k, StringComparison.Ordinal))) return false;

        var mentionsBank = subdomains.Any(l => SafetyLists.FinancialKeywords.Any(k => l.Contains(k, StringComparison.Ordinal)));
        var looksDeceptive = subdomains.Any(l => l is "com" or "net" or "org" or "co") ||
                             SafetyLists.SuspiciousTlds.Contains(labels[^1]) || SafetyLists.HighRiskTlds.Contains(labels[^1]);
        return mentionsBank && looksDeceptive;
    }

    private static string ToSkeleton(string host)
    {
        var sb = new StringBuilder(host.Length);
        foreach (var c in host.ToLowerInvariant())
            sb.Append(SafetyLists.Confusables.TryGetValue(c, out var latin) ? latin : c);
        return sb.ToString();
    }

    private static string Deleet(string token)
    {
        if (!token.Any(char.IsAsciiLetter)) return token;
        var sb = new StringBuilder(token.Length);
        foreach (var c in token)
            sb.Append(SafetyLists.Leet.TryGetValue(c, out var letter) ? letter : c);
        return sb.ToString().Replace("rn", "m", StringComparison.Ordinal).Replace("vv", "w", StringComparison.Ordinal);
    }

    // ---- Scripts (IDN homographs) ----

    private enum Script { Neutral, Latin, Cyrillic, Greek, Other }

    private static Script ScriptOf(char c)
    {
        if (char.IsAsciiLetter(c)) return Script.Latin;
        if (c < 0x80) return Script.Neutral;
        if (c is (>= 'À' and <= 'ɏ' and not '×' and not '÷') or (>= 'Ḁ' and <= 'ỿ')) return Script.Latin;
        if (c is (>= 'Ѐ' and <= 'ԯ') or (>= 'ⷠ' and <= 'ⷿ') or (>= 'Ꙁ' and <= 'ꚟ')) return Script.Cyrillic;
        if (c is (>= 'Ͱ' and <= 'Ͽ') or (>= 'ἀ' and <= '῿')) return Script.Greek;
        return Script.Other;
    }

    private static bool HasMixedScripts(string[] labels)
    {
        var hasLatinLabel = false;
        var hasConfusableLabel = false;

        foreach (var label in labels)
        {
            var scripts = label.Select(ScriptOf).Where(s => s is not Script.Neutral).Distinct().ToList();
            var latin = scripts.Contains(Script.Latin);
            var cyrillic = scripts.Contains(Script.Cyrillic);
            var greek = scripts.Contains(Script.Greek);

            if ((latin && (cyrillic || greek)) || (cyrillic && greek)) return true;
            if (latin && scripts.Count == 1) hasLatinLabel = true;

            // A whole label written in Cyrillic/Greek letters that all look Latin ("аррӏе").
            if ((cyrillic || greek) && scripts.Count == 1 &&
                label.Where(c => ScriptOf(c) is not Script.Neutral).All(SafetyLists.Confusables.ContainsKey))
            {
                hasConfusableLabel = true;
            }
        }

        return hasLatinLabel && hasConfusableLabel;
    }

    // ---- Helpers ----

    private static string RegistrableDomain(string[] labels)
    {
        if (labels.Length <= 2) return string.Join('.', labels);
        var lastTwo = labels[^2] + "." + labels[^1];
        return SafetyLists.MultiPartSuffixes.Contains(lastTwo) ? labels[^3] + "." + lastTwo : lastTwo;
    }

    private static string SafeIdnHost(Uri uri)
    {
        try
        {
            return uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        }
        catch (InvalidOperationException)
        {
            return uri.Host.ToLowerInvariant().TrimEnd('.');
        }
    }

    private static string ToUnicode(string asciiHost)
    {
        if (!asciiHost.Contains("xn--", StringComparison.OrdinalIgnoreCase)) return asciiHost;
        try
        {
            // IdnMapping instances are not guaranteed thread-safe; the analyzer is a singleton.
            return new IdnMapping().GetUnicode(asciiHost);
        }
        catch (ArgumentException)
        {
            return asciiHost;
        }
    }

    private static bool TryParseWithDecodedHost(string candidate, out Uri uri)
    {
        uri = null!;
        var rawHost = RawHost(candidate);
        if (!rawHost.Contains('%', StringComparison.Ordinal)) return false;
        var start = candidate.IndexOf(rawHost, candidate.IndexOf("://", StringComparison.Ordinal) + 3, StringComparison.Ordinal);
        if (start < 0) return false;
        var decoded = string.Concat(candidate.AsSpan(0, start), Uri.UnescapeDataString(rawHost), candidate.AsSpan(start + rawHost.Length));
        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var parsed)) return false;
        uri = parsed;
        return true;
    }

    /// <summary>The host exactly as written (before .NET normalises numeric IPs or percent-escapes).</summary>
    private static string RawHost(string candidate)
    {
        var start = candidate.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var authority = candidate[(start + 3)..];
        var end = authority.IndexOfAny(['/', '?', '#', '\\']);
        if (end >= 0) authority = authority[..end];
        var at = authority.LastIndexOf('@');
        if (at >= 0) authority = authority[(at + 1)..];
        if (authority.StartsWith('[')) return authority; // IPv6 literal
        var colon = authority.LastIndexOf(':');
        if (colon >= 0) authority = authority[..colon];
        return authority.TrimEnd('.');
    }

    private static string? FileExtension(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        var segment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var dot = segment.LastIndexOf('.');
        return dot <= 0 || dot == segment.Length - 1 ? null : Uri.UnescapeDataString(segment[(dot + 1)..]).ToLowerInvariant();
    }

    private static string RemoveControlCharacters(string value)
    {
        if (!value.Any(c => char.IsControl(c) || c is '​' or '‌' or '‍' or '⁠' or '﻿')) return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c) && c is not ('​' or '‌' or '‍' or '⁠' or '﻿')) sb.Append(c);
        }
        return sb.ToString();
    }

    private static SafetySignal Signal(string code, string description, RiskLevel severity) => new(code, description, severity);

    private static UrlSafetyReport Report(string url, List<SafetySignal> signals, string? displayHost, SafetySignal? extra = null)
    {
        if (extra is not null) signals.Add(extra);
        var level = signals.Count == 0 ? RiskLevel.Safe : signals.Max(s => s.Severity);
        return new UrlSafetyReport(url, level, signals, displayHost);
    }
}
