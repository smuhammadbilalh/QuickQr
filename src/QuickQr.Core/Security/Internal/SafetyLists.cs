using System.Collections.Frozen;

namespace QuickQr.Core.Security.Internal;

/// <summary>Static, frozen reference data for <see cref="UrlSafetyAnalyzer"/>. Kept small and offline by design.</summary>
internal static class SafetyLists
{
    public static readonly FrozenSet<string> DangerousSchemes = new[]
    {
        "javascript", "data", "file", "vbscript", "intent", "blob", "jar", "view-source",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Link shorteners and QR redirectors that hide the final destination.</summary>
    public static readonly FrozenSet<string> Shorteners = new[]
    {
        "bit.ly", "bitly.com", "j.mp", "tinyurl.com", "tiny.one", "t.co", "goo.gl", "ow.ly", "is.gd", "v.gd", "buff.ly", "rebrand.ly",
        "cutt.ly", "shorturl.at", "rb.gy", "tiny.cc", "s.id", "t.ly", "lnkd.in", "qrco.de", "bl.ink", "short.io", "short.gy", "soo.gd",
        "x.gd", "clck.ru", "shorte.st", "adf.ly", "bit.do", "mcaf.ee", "su.pr", "tr.im", "db.tt", "qr.ae", "u.to", "1url.com",
        "shrtco.de", "qrs.ly", "l.ead.me", "me-qr.com", "qr.net", "urlz.fr", "rotf.lol", "n9.cl", "gg.gg", "tny.im", "amzn.to",
        "dub.sh", "lnk.bio", "spoo.me", "surl.li", "zpr.io",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>TLDs heavily abused for phishing, or confusable with file names (.zip, .mov).</summary>
    public static readonly FrozenSet<string> HighRiskTlds = new[]
    {
        "zip", "mov", "tk", "ml", "ga", "cf", "gq",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> SuspiciousTlds = new[]
    {
        "xyz", "top", "click", "country", "kim", "work", "rest", "fit", "support", "cam", "cyou", "icu", "buzz", "monster", "quest",
        "sbs", "cfd", "bond", "lol", "mom", "gdn", "loan", "men", "date", "faith", "racing", "review", "stream", "download", "win",
        "bid", "trade", "party", "science", "accountant", "cricket", "webcam", "beauty", "hair", "skin", "boats", "autos",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executable / installer extensions that a QR code should rarely link to directly.</summary>
    public static readonly FrozenSet<string> DownloadExtensions = new[]
    {
        "apk", "xapk", "apks", "aab", "exe", "msi", "msix", "appx", "dmg", "pkg", "ipa", "bat", "cmd", "scr", "js", "jse", "vbs",
        "ps1", "jar", "hta", "lnk", "deb", "rpm", "iso",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Second-level public suffixes so "evil.co.uk" is treated as the registrable domain, not "co.uk".</summary>
    public static readonly FrozenSet<string> MultiPartSuffixes = new[]
    {
        "co.uk", "org.uk", "me.uk", "ac.uk", "gov.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au",
        "co.nz", "org.nz", "net.nz", "govt.nz", "ac.nz",
        "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "gr.jp",
        "co.kr", "or.kr", "go.kr", "ac.kr",
        "co.in", "net.in", "org.in", "firm.in", "gen.in", "ind.in", "gov.in", "ac.in",
        "com.br", "net.br", "org.br", "gov.br",
        "com.mx", "org.mx", "gob.mx", "com.ar", "com.co", "com.pe", "com.ve", "com.uy", "com.ec", "com.bo", "com.py",
        "com.cn", "net.cn", "org.cn", "gov.cn", "com.hk", "org.hk", "com.tw", "org.tw", "com.sg", "edu.sg", "gov.sg",
        "com.my", "co.id", "or.id", "co.th", "in.th", "com.ph", "com.vn", "com.pk", "com.bd", "com.np", "com.lk",
        "com.tr", "org.tr", "com.sa", "com.eg", "com.qa", "com.kw", "com.om", "com.bh", "co.il", "org.il", "ac.il",
        "co.za", "org.za", "gov.za", "com.ng", "co.ke", "or.ke", "co.tz", "co.ug", "com.gh",
        "com.ua", "com.pl", "com.ru", "com.es", "com.pt", "com.gr", "co.at", "or.at", "com.mt", "com.cy",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Frequently impersonated brands and the domains they actually own (any TLD of "brand.*" also counts).</summary>
    public static readonly FrozenDictionary<string, FrozenSet<string>> Brands = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["paypal"] = ["paypal.com", "paypal.me", "paypalobjects.com", "paypal-community.com"],
        ["apple"] = ["apple.com", "icloud.com", "apple.co", "me.com", "mzstatic.com", "apple.news"],
        ["icloud"] = ["icloud.com", "apple.com"],
        ["microsoft"] = ["microsoft.com", "live.com", "office.com", "office365.com", "outlook.com", "microsoftonline.com", "sharepoint.com",
            "onedrive.com", "azure.com", "windows.net", "windows.com", "xbox.com", "bing.com", "msn.com", "skype.com", "aka.ms", "microsoft365.com"],
        ["office365"] = ["office365.com", "office.com", "microsoft.com"],
        ["outlook"] = ["outlook.com", "live.com", "office.com", "microsoft.com"],
        ["google"] = ["google.com", "youtube.com", "gmail.com", "googleusercontent.com", "googleapis.com", "gstatic.com", "goo.gl", "g.co",
            "blogspot.com", "withgoogle.com", "googleblog.com", "android.com", "googlesource.com"],
        ["gmail"] = ["gmail.com", "google.com"],
        ["amazon"] = ["amazon.com", "amazonaws.com", "amzn.to", "a.co", "amazon.dev", "aboutamazon.com", "primevideo.com", "media-amazon.com"],
        ["netflix"] = ["netflix.com", "netflix.net", "nflxext.com", "nflximg.net"],
        ["facebook"] = ["facebook.com", "fb.com", "fb.me", "facebook.net", "meta.com", "messenger.com", "fbcdn.net"],
        ["instagram"] = ["instagram.com", "instagr.am", "cdninstagram.com"],
        ["whatsapp"] = ["whatsapp.com", "wa.me", "whatsapp.net"],
        ["linkedin"] = ["linkedin.com", "lnkd.in", "licdn.com"],
        ["dhl"] = ["dhl.com", "dhl.de", "dhl.co.uk", "dpdhl.com"],
        ["fedex"] = ["fedex.com"],
        ["usps"] = ["usps.com"],
        ["coinbase"] = ["coinbase.com"],
        ["binance"] = ["binance.com", "binance.us"],
        ["metamask"] = ["metamask.io"],
    }.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToFrozenSet(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);

    /// <summary>Words commonly glued to a brand in phishing hosts ("paypal-login", "mypaypal", "applesupport").</summary>
    public static readonly FrozenSet<string> PhishingAffixes = new[]
    {
        "login", "logon", "signin", "secure", "security", "verify", "verification", "account", "accounts", "support", "help", "update",
        "service", "services", "id", "pay", "billing", "auth", "online", "web", "my", "app", "mail", "team", "center", "official", "alert",
        "confirm", "unlock", "wallet", "refund", "store", "customer", "care",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Generic financial keywords: suspicious in a subdomain of an unrelated site.</summary>
    public static readonly string[] FinancialKeywords = ["bank", "banking"];

    /// <summary>Cyrillic and Greek letters that render like Latin letters, mapped to their Latin look-alike.</summary>
    public static readonly FrozenDictionary<char, char> Confusables = new Dictionary<char, char>
    {
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['у'] = 'y', ['х'] = 'x',
        ['ѕ'] = 's', ['і'] = 'i', ['ј'] = 'j', ['һ'] = 'h', ['ԁ'] = 'd', ['ԛ'] = 'q', ['ԝ'] = 'w',
        ['ӏ'] = 'l',
        ['α'] = 'a', ['ο'] = 'o', ['ν'] = 'v', ['ρ'] = 'p', ['ι'] = 'i', ['κ'] = 'k', ['υ'] = 'u',
    }.ToFrozenDictionary();

    /// <summary>Digit/letter substitutions used to dodge brand matching ("paypa1", "g00gle").</summary>
    public static readonly FrozenDictionary<char, char> Leet = new Dictionary<char, char>
    {
        ['0'] = 'o', ['1'] = 'l', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['$'] = 's', ['@'] = 'a',
    }.ToFrozenDictionary();
}
