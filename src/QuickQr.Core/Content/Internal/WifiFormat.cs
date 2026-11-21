using System.Text;

namespace QuickQr.Core.Content.Internal;

/// <summary>Wi-Fi network configuration: <c>WIFI:T:WPA;S:ssid;P:password;H:true;;</c> (ZXing / Android / iOS format).</summary>
internal static class WifiFormat
{
    public const string Prefix = "WIFI:";
    private const string Specials = "\\;,:\"";

    public static string Write(WifiContent content)
    {
        var sb = new StringBuilder(Prefix);
        sb.Append("T:").Append(SecurityToken(content.Security)).Append(';');
        sb.Append("S:").Append(PayloadText.BackslashEscape(content.Ssid ?? string.Empty, Specials)).Append(';');
        if (content.Security != WifiSecurity.None && !string.IsNullOrEmpty(content.Password))
            sb.Append("P:").Append(PayloadText.BackslashEscape(content.Password, Specials)).Append(';');
        if (content.Hidden) sb.Append("H:true;");
        sb.Append(';');
        return sb.ToString();
    }

    public static string SecurityToken(WifiSecurity security) => security switch
    {
        WifiSecurity.None => "nopass",
        WifiSecurity.Wep => "WEP",
        WifiSecurity.Wpa3 => "SAE",
        _ => "WPA",
    };

    public static string SecurityDisplayName(WifiSecurity security) => security switch
    {
        WifiSecurity.None => "Open",
        WifiSecurity.Wep => "WEP",
        WifiSecurity.Wpa3 => "WPA3",
        _ => "WPA2",
    };

    public static Error? Validate(WifiContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Ssid))
            return Error.Validation("Enter the network name (SSID).");
        if (Encoding.UTF8.GetByteCount(content.Ssid) > 32)
            return Error.Validation("The network name can be at most 32 bytes long.");

        var password = content.Password ?? string.Empty;
        switch (content.Security)
        {
            case WifiSecurity.Wpa or WifiSecurity.Wpa3:
                if (password.Length == 0)
                    return Error.Validation("Enter the Wi-Fi password, or choose an open network.");
                if (password.Length is < 8 or > 63)
                    return Error.Validation("WPA passwords must be 8 to 63 characters long.");
                break;
            case WifiSecurity.Wep:
                var isHex = password.All(Uri.IsHexDigit);
                if (password.Length is not (5 or 13) && !(isHex && password.Length is 10 or 26))
                    return Error.Validation("WEP keys must be 5 or 13 characters, or 10 or 26 hexadecimal digits.");
                break;
            case WifiSecurity.None:
                break;
            default:
                return Error.Validation("Choose a supported Wi-Fi security type.");
        }
        return null;
    }

    public static QrContent? TryParse(string payload)
    {
        string? ssid = null;
        string? password = null;
        string? type = null;
        var hidden = false;

        foreach (var (key, raw) in PayloadText.ParseFields(payload[Prefix.Length..]))
        {
            switch (key)
            {
                case "S": ssid ??= Unquote(raw); break;
                case "P": password ??= Unquote(raw); break;
                case "T": type ??= Unquote(raw).Trim(); break;
                case "H":
                    var h = Unquote(raw).Trim();
                    hidden = h.Equals("true", StringComparison.OrdinalIgnoreCase) || h == "1";
                    break;
            }
        }

        if (string.IsNullOrEmpty(ssid)) return null;

        var security = ParseSecurity(type, password);
        if (security == WifiSecurity.None || string.IsNullOrEmpty(password)) password = null;
        return new WifiContent(ssid, password, security, hidden);
    }

    private static WifiSecurity ParseSecurity(string? type, string? password)
    {
        if (string.IsNullOrEmpty(type))
            return string.IsNullOrEmpty(password) ? WifiSecurity.None : WifiSecurity.Wpa;

        var t = type.ToUpperInvariant();
        return t switch
        {
            "NOPASS" or "NONE" or "OPEN" => WifiSecurity.None,
            "WEP" => WifiSecurity.Wep,
            "SAE" or "WPA3" or "WPA3-SAE" or "WPA3-PERSONAL" => WifiSecurity.Wpa3,
            _ when t.StartsWith("WPA", StringComparison.Ordinal) => WifiSecurity.Wpa,
            _ => string.IsNullOrEmpty(password) ? WifiSecurity.None : WifiSecurity.Wpa,
        };
    }

    /// <summary>Removes optional surrounding (unescaped) double quotes, then backslash-unescapes.</summary>
    private static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' && !IsEscapedAt(raw, raw.Length - 1))
            raw = raw[1..^1];
        return PayloadText.BackslashUnescape(raw);
    }

    private static bool IsEscapedAt(string value, int index)
    {
        var backslashes = 0;
        for (var i = index - 1; i >= 0 && value[i] == '\\'; i--) backslashes++;
        return backslashes % 2 == 1;
    }
}
