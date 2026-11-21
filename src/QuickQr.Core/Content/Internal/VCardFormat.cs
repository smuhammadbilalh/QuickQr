namespace QuickQr.Core.Content.Internal;

/// <summary>vCard 3.0 writer (RFC 2426 / RFC 6350 escaping, CRLF, no folding) and a tolerant 2.1/3.0/4.0 reader.</summary>
internal static class VCardFormat
{
    public const string Begin = "BEGIN:VCARD";

    public static string Write(ContactContent content)
    {
        var first = PayloadText.Clean(content.FirstName);
        var last = PayloadText.Clean(content.LastName);
        var organization = PayloadText.Clean(content.Organization);
        var fullName = string.Join(' ', new[] { first, last }.Where(s => s is not null));
        if (fullName.Length == 0) fullName = organization ?? string.Empty;

        var lines = new List<string>
        {
            Begin,
            "VERSION:3.0",
            $"N:{Esc(last)};{Esc(first)};;;",
            $"FN:{Esc(fullName)}",
        };

        if (organization is not null) lines.Add($"ORG:{Esc(organization)}");
        if (PayloadText.Clean(content.JobTitle) is { } title) lines.Add($"TITLE:{Esc(title)}");

        foreach (var phone in content.Phones ?? [])
        {
            if (PayloadText.Clean(phone?.Number) is { } number)
                lines.Add($"TEL;TYPE={PhoneType(phone!.Kind)}:{Esc(number)}");
        }

        foreach (var email in content.Emails ?? [])
        {
            if (PayloadText.Clean(email) is { } address)
                lines.Add($"EMAIL;TYPE=INTERNET:{Esc(address)}");
        }

        if (PayloadText.Clean(content.Website) is { } website) lines.Add($"URL:{PayloadText.SingleLine(website)}");

        if (content.Address is { } a && HasAny(a))
        {
            lines.Add($"ADR;TYPE=WORK:;;{Esc(a.Street)};{Esc(a.City)};{Esc(a.Region)};{Esc(a.PostalCode)};{Esc(a.Country)}");
        }

        if (PayloadText.Clean(content.Note) is { } note) lines.Add($"NOTE:{Esc(note)}");
        lines.Add("END:VCARD");
        return string.Join("\r\n", lines);
    }

    /// <summary>Converts a MeCard to an equivalent vCard 3.0 (used to add MeCard contacts to the address book).</summary>
    public static string Write(MeCardContent meCard)
    {
        var (first, last) = SplitName(meCard.Name);
        var contact = new ContactContent
        {
            FirstName = first,
            LastName = last,
            Phones = PayloadText.Clean(meCard.Phone) is { } phone ? [new ContactPhone(phone)] : [],
            Emails = PayloadText.Clean(meCard.Email) is { } email ? [email] : [],
            Website = meCard.Url,
            Address = PayloadText.Clean(meCard.Address) is { } address ? new PostalAddress(Street: address) : null,
            Note = meCard.Note,
        };
        return Write(contact);
    }

    /// <summary>"John Doe" → ("John", "Doe"); a single word becomes the first name.</summary>
    public static (string? First, string? Last) SplitName(string? name)
    {
        var clean = PayloadText.Clean(name);
        if (clean is null) return (null, null);
        var space = clean.LastIndexOf(' ');
        return space <= 0 ? (clean, null) : (clean[..space].Trim(), clean[(space + 1)..]);
    }

    private static bool HasAny(PostalAddress a) =>
        PayloadText.Clean(a.Street) is not null || PayloadText.Clean(a.City) is not null || PayloadText.Clean(a.Region) is not null ||
        PayloadText.Clean(a.PostalCode) is not null || PayloadText.Clean(a.Country) is not null;

    private static string Esc(string? value) => PayloadText.Clean(value) is { } v ? PayloadText.EscapeText(v) : string.Empty;

    private static string PhoneType(PhoneKind kind) => kind switch
    {
        PhoneKind.Mobile => "CELL",
        PhoneKind.Work => "WORK",
        PhoneKind.Home => "HOME",
        PhoneKind.Fax => "FAX",
        _ => "VOICE",
    };

    public static Error? Validate(ContactContent content)
    {
        if (string.IsNullOrWhiteSpace(content.FirstName) && string.IsNullOrWhiteSpace(content.LastName) &&
            string.IsNullOrWhiteSpace(content.Organization))
            return Error.Validation("Enter a name or an organisation.");

        foreach (var phone in content.Phones ?? [])
        {
            if (phone is null || string.IsNullOrWhiteSpace(phone.Number)) continue;
            if (Validators.NormalizePhone(phone.Number) is null)
                return Error.Validation($"“{phone.Number.Trim()}” isn't a valid phone number.");
        }

        foreach (var email in content.Emails ?? [])
        {
            if (string.IsNullOrWhiteSpace(email)) continue;
            if (!Validators.IsEmail(email.Trim()))
                return Error.Validation($"“{email.Trim()}” isn't a valid email address.");
        }

        return null;
    }

    public static QrContent TryParse(string payload)
    {
        string? first = null, last = null, formatted = null, organization = null, title = null, website = null, note = null;
        PostalAddress? address = null;
        var phones = new List<ContactPhone>();
        var emails = new List<string>();

        foreach (var raw in ContentLines.Unfold(payload))
        {
            if (ContentLines.Parse(raw) is not { } line) continue;
            if (line.Name == "END" && line.Value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase)) break;

            switch (line.Name)
            {
                case "N":
                    var n = SplitComponents(line.Value);
                    last = Component(n, 0);
                    var given = Component(n, 1);
                    var middle = Component(n, 2);
                    first = middle is null ? given : PayloadText.Clean($"{given} {middle}");
                    break;
                case "FN":
                    formatted ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value));
                    break;
                case "ORG":
                    organization ??= PayloadText.Clean(string.Join(", ", SplitComponents(line.Value).Where(s => s.Length > 0)));
                    break;
                case "TITLE":
                    title ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value));
                    break;
                case "TEL":
                    var number = PayloadText.Clean(StripUriScheme(PayloadText.UnescapeText(line.Value), "tel:"));
                    if (number is not null) phones.Add(new ContactPhone(number, PhoneKindOf(line)));
                    break;
                case "EMAIL":
                    var email = PayloadText.Clean(StripUriScheme(PayloadText.UnescapeText(line.Value), "mailto:"));
                    if (email is not null) emails.Add(email);
                    break;
                case "URL":
                    website ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value));
                    break;
                case "ADR":
                    address ??= ParseAddress(line.Value);
                    break;
                case "NOTE":
                    note ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value));
                    break;
            }
        }

        if (first is null && last is null && formatted is not null &&
            !string.Equals(formatted, organization, StringComparison.Ordinal))
        {
            first = formatted;
        }

        return new ContactContent
        {
            FirstName = first,
            LastName = last,
            Organization = organization,
            JobTitle = title,
            Phones = phones,
            Emails = emails,
            Website = website,
            Address = address,
            Note = note,
        };
    }

    private static List<string> SplitComponents(string value) =>
        [.. PayloadText.SplitUnescaped(value, ';').Select(p => PayloadText.UnescapeText(p).Trim())];

    private static string? Component(List<string> parts, int index) => index < parts.Count ? PayloadText.Clean(parts[index]) : null;

    private static PostalAddress? ParseAddress(string value)
    {
        // ADR: PO box ; extended ; street ; locality ; region ; postal code ; country
        var parts = SplitComponents(value);
        var street = string.Join(", ", new[] { Component(parts, 0), Component(parts, 1), Component(parts, 2) }.Where(s => s is not null));
        var address = new PostalAddress(
            Street: PayloadText.Clean(street),
            City: Component(parts, 3),
            Region: Component(parts, 4),
            PostalCode: Component(parts, 5),
            Country: Component(parts, 6));
        return HasAny(address) ? address : null;
    }

    private static string StripUriScheme(string value, string scheme)
    {
        var trimmed = value.Trim();
        return PayloadText.StartsWithIgnoreCase(trimmed, scheme) ? trimmed[scheme.Length..] : trimmed;
    }

    private static PhoneKind PhoneKindOf(ContentLine line)
    {
        if (line.HasType("FAX")) return PhoneKind.Fax;
        if (line.HasType("CELL") || line.HasType("MOBILE") || line.HasType("IPHONE")) return PhoneKind.Mobile;
        if (line.HasType("WORK")) return PhoneKind.Work;
        if (line.HasType("HOME")) return PhoneKind.Home;
        return PhoneKind.Other;
    }

    /// <summary>Plain-text block for copying contact details.</summary>
    public static string Describe(ContactContent content)
    {
        var lines = new List<string?>
        {
            PayloadText.Clean(content.FullName),
            PayloadText.Clean(content.Organization),
            PayloadText.Clean(content.JobTitle),
        };
        lines.AddRange((content.Phones ?? []).Select(p => PayloadText.Clean(p.Number)));
        lines.AddRange((content.Emails ?? []).Select(PayloadText.Clean));
        lines.Add(PayloadText.Clean(content.Website));
        if (content.Address is { } a)
        {
            var parts = new[] { a.Street, a.City, a.Region, a.PostalCode, a.Country }.Select(PayloadText.Clean).Where(s => s is not null);
            lines.Add(PayloadText.Clean(string.Join(", ", parts)));
        }
        lines.Add(PayloadText.Clean(content.Note));
        return string.Join('\n', lines.Where(l => l is not null));
    }
}
