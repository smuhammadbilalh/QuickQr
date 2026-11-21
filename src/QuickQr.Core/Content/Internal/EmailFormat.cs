namespace QuickQr.Core.Content.Internal;

/// <summary>Email: RFC 6068 <c>mailto:</c> URIs (written) plus Docomo <c>MATMSG:</c> (read).</summary>
internal static class EmailFormat
{
    public const string MailtoPrefix = "mailto:";
    public const string MatmsgPrefix = "MATMSG:";

    public static string Write(EmailContent content)
    {
        var to = EncodeAddresses(content.To);
        var query = new List<string>(4);
        if (PayloadText.Clean(content.Subject) is { } subject)
            query.Add("subject=" + PayloadText.PercentEncode(PayloadText.SingleLine(subject)));
        if (!string.IsNullOrEmpty(content.Body))
            query.Add("body=" + PayloadText.PercentEncode(PayloadText.NormalizeNewlines(content.Body).Replace("\n", "\r\n", StringComparison.Ordinal)));
        if (Validators.SplitAddresses(content.Cc).Count > 0) query.Add("cc=" + EncodeAddresses(content.Cc));
        if (Validators.SplitAddresses(content.Bcc).Count > 0) query.Add("bcc=" + EncodeAddresses(content.Bcc));

        return query.Count == 0 ? MailtoPrefix + to : $"{MailtoPrefix}{to}?{string.Join('&', query)}";
    }

    /// <summary>mailto URI for a single address (used by contact actions).</summary>
    public static string MailtoFor(string address) => MailtoPrefix + EncodeAddresses(address);

    private static string EncodeAddresses(string? value) =>
        string.Join(',', Validators.SplitAddresses(value).Select(a => PayloadText.PercentEncode(a, "@")));

    public static Error? Validate(EmailContent content)
    {
        var to = Validators.SplitAddresses(content.To);
        if (to.Count == 0)
            return Error.Validation("Enter the recipient's email address.");
        foreach (var address in to.Concat(Validators.SplitAddresses(content.Cc)).Concat(Validators.SplitAddresses(content.Bcc)))
        {
            if (!Validators.IsEmail(address))
                return Error.Validation($"“{address}” isn't a valid email address.");
        }
        return null;
    }

    public static QrContent? TryParseMailto(string payload)
    {
        var rest = payload[MailtoPrefix.Length..];
        if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];

        var questionMark = rest.IndexOf('?', StringComparison.Ordinal);
        var toPart = PayloadText.PercentDecode(questionMark < 0 ? rest : rest[..questionMark]);
        var query = questionMark < 0 ? [] : PayloadText.ParseQuery(rest[(questionMark + 1)..], plusAsSpace: false);

        var recipients = Validators.SplitAddresses(toPart);
        recipients.AddRange(query.Where(p => p.Key.Equals("to", StringComparison.OrdinalIgnoreCase)).SelectMany(p => Validators.SplitAddresses(p.Value)));

        var subject = PayloadText.GetValue(query, "subject");
        var body = PayloadText.GetValue(query, "body");
        var cc = PayloadText.GetValue(query, "cc");
        var bcc = PayloadText.GetValue(query, "bcc");

        return new EmailContent(
            string.Join(',', recipients),
            NullIfEmpty(subject),
            string.IsNullOrEmpty(body) ? null : PayloadText.NormalizeNewlines(body),
            JoinAddresses(cc),
            JoinAddresses(bcc));
    }

    public static QrContent? TryParseMatmsg(string payload)
    {
        string? to = null, subject = null, body = null;
        foreach (var (key, raw) in PayloadText.ParseFields(payload[MatmsgPrefix.Length..]))
        {
            var value = PayloadText.BackslashUnescape(raw);
            switch (key)
            {
                case "TO": to ??= value.Trim(); break;
                case "SUB": subject ??= value; break;
                case "BODY": body ??= value; break;
            }
        }

        return string.IsNullOrWhiteSpace(to) && subject is null && body is null
            ? null
            : new EmailContent(to ?? string.Empty, NullIfEmpty(subject), string.IsNullOrEmpty(body) ? null : PayloadText.NormalizeNewlines(body));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? JoinAddresses(string? value)
    {
        var list = Validators.SplitAddresses(value);
        return list.Count == 0 ? null : string.Join(',', list);
    }
}
