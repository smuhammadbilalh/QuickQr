using System.Text;

namespace QuickQr.Core.Content.Internal;

/// <summary>NTT Docomo MeCard: <c>MECARD:N:Doe,John;TEL:..;EMAIL:..;URL:..;ADR:..;NOTE:..;;</c>.</summary>
internal static class MeCardFormat
{
    public const string Prefix = "MECARD:";
    private const string Specials = "\\;,:";

    public static string Escape(string value) => PayloadText.BackslashEscape(value, Specials);

    public static string Write(MeCardContent content)
    {
        var sb = new StringBuilder(Prefix);
        Append(sb, "N", content.Name);
        Append(sb, "TEL", content.Phone);
        Append(sb, "EMAIL", content.Email);
        Append(sb, "URL", content.Url);
        Append(sb, "ADR", content.Address);
        Append(sb, "NOTE", content.Note);
        sb.Append(';');
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string key, string? value)
    {
        if (PayloadText.Clean(value) is { } v)
            sb.Append(key).Append(':').Append(Escape(PayloadText.SingleLine(v))).Append(';');
    }

    public static Error? Validate(MeCardContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Name))
            return Error.Validation("Enter a name.");
        if (!string.IsNullOrWhiteSpace(content.Phone) && Validators.NormalizePhone(content.Phone) is null)
            return Error.Validation($"“{content.Phone.Trim()}” isn't a valid phone number.");
        if (!string.IsNullOrWhiteSpace(content.Email) && !Validators.IsEmail(content.Email.Trim()))
            return Error.Validation($"“{content.Email.Trim()}” isn't a valid email address.");
        return null;
    }

    public static QrContent? TryParse(string payload)
    {
        string? name = null, phone = null, email = null, url = null, address = null, note = null;
        foreach (var (key, raw) in PayloadText.ParseFields(payload[Prefix.Length..]))
        {
            switch (key)
            {
                case "N": name ??= ParseName(raw); break;
                case "TEL": phone ??= Value(raw); break;
                case "EMAIL": email ??= Value(raw); break;
                case "URL": url ??= Value(raw); break;
                case "ADR": address ??= Value(raw); break;
                case "NOTE": note ??= Value(raw); break;
            }
        }

        return name is null && phone is null && email is null
            ? null
            : new MeCardContent(name ?? string.Empty, phone, email, url, address, note);
    }

    private static string? Value(string raw) => PayloadText.Clean(PayloadText.BackslashUnescape(raw));

    /// <summary>MeCard names are "Last,First"; an escaped comma is part of the name.</summary>
    private static string? ParseName(string raw)
    {
        var parts = PayloadText.SplitUnescaped(raw, ',');
        if (parts.Count == 2)
        {
            var last = PayloadText.Clean(PayloadText.BackslashUnescape(parts[0]));
            var first = PayloadText.Clean(PayloadText.BackslashUnescape(parts[1]));
            return PayloadText.Clean(string.Join(' ', new[] { first, last }.Where(s => s is not null)));
        }
        return Value(raw);
    }
}
