using System.Globalization;
using System.Text;
using QuickQr.Core.Content;
using ContactPhone = QuickQr.Core.Content.ContactPhone;

namespace QuickQr.Services.Actions;

/// <summary>Builds the vCard / iCalendar documents handed to the contacts and calendar apps.</summary>
internal static class ContentDocuments
{
    private const string Crlf = "\r\n";

    public static string? VCardFor(QrContent content, string? target)
    {
        if (target is not null && target.TrimStart().StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            return target.Trim();

        return content switch
        {
            ContactContent c => VCard(c),
            MeCardContent m => VCard(MeCardToContact(m)),
            PhoneContent p => VCard(new ContactContent { Phones = new[] { new ContactPhone(p.Number) } }),
            EmailContent e => VCard(new ContactContent { Emails = new[] { e.To } }),
            _ => null,
        };
    }

    public static string? CalendarFor(QrContent content, string? target)
    {
        if (target is not null)
        {
            var trimmed = target.Trim();
            if (trimmed.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            if (trimmed.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                return "BEGIN:VCALENDAR" + Crlf + "VERSION:2.0" + Crlf + "PRODID:-//QuickQr//EN" + Crlf + trimmed + Crlf + "END:VCALENDAR";
        }

        return content is CalendarEventContent ev ? Calendar(ev) : null;
    }

    public static string VCard(ContactContent c)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCARD");
        Line(sb, "VERSION:3.0");
        var fullName = c.FullName;
        if (string.IsNullOrWhiteSpace(fullName)) fullName = c.Organization ?? c.Phones.FirstOrDefault()?.Number ?? c.Emails.FirstOrDefault() ?? "Contact";
        Line(sb, $"N:{Escape(c.LastName)};{Escape(c.FirstName)};;;");
        Line(sb, $"FN:{Escape(fullName)}");
        if (!string.IsNullOrWhiteSpace(c.Organization)) Line(sb, $"ORG:{Escape(c.Organization)}");
        if (!string.IsNullOrWhiteSpace(c.JobTitle)) Line(sb, $"TITLE:{Escape(c.JobTitle)}");

        foreach (var phone in c.Phones.Where(p => !string.IsNullOrWhiteSpace(p.Number)))
        {
            var type = phone.Kind switch
            {
                PhoneKind.Work => "WORK,VOICE",
                PhoneKind.Home => "HOME,VOICE",
                PhoneKind.Fax => "FAX",
                PhoneKind.Other => "VOICE",
                _ => "CELL",
            };
            Line(sb, $"TEL;TYPE={type}:{Escape(phone.Number)}");
        }

        foreach (var email in c.Emails.Where(e => !string.IsNullOrWhiteSpace(e)))
            Line(sb, $"EMAIL;TYPE=INTERNET:{Escape(email)}");

        if (!string.IsNullOrWhiteSpace(c.Website)) Line(sb, $"URL:{Escape(c.Website)}");

        if (c.Address is { } a)
            Line(sb, $"ADR;TYPE=WORK:;;{Escape(a.Street)};{Escape(a.City)};{Escape(a.Region)};{Escape(a.PostalCode)};{Escape(a.Country)}");

        if (!string.IsNullOrWhiteSpace(c.Note)) Line(sb, $"NOTE:{Escape(c.Note)}");
        Line(sb, "END:VCARD");
        return sb.ToString();
    }

    public static string Calendar(CalendarEventContent ev)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//QuickQr//EN");
        Line(sb, "BEGIN:VEVENT");
        Line(sb, $"UID:{Guid.NewGuid():N}@quickqr");
        Line(sb, $"DTSTAMP:{UtcStamp(DateTimeOffset.UtcNow)}");
        if (ev.AllDay)
        {
            var end = ev.End.Date > ev.Start.Date ? ev.End.Date : ev.Start.Date.AddDays(1);
            Line(sb, $"DTSTART;VALUE=DATE:{ev.Start.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
            Line(sb, $"DTEND;VALUE=DATE:{end.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        }
        else
        {
            Line(sb, $"DTSTART:{UtcStamp(ev.Start)}");
            Line(sb, $"DTEND:{UtcStamp(ev.End > ev.Start ? ev.End : ev.Start.AddHours(1))}");
        }

        Line(sb, $"SUMMARY:{Escape(ev.Title)}");
        if (!string.IsNullOrWhiteSpace(ev.Location)) Line(sb, $"LOCATION:{Escape(ev.Location)}");
        if (!string.IsNullOrWhiteSpace(ev.Description)) Line(sb, $"DESCRIPTION:{Escape(ev.Description)}");
        Line(sb, "END:VEVENT");
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    public static ContactContent MeCardToContact(MeCardContent m)
    {
        // MECARD names are "Last,First".
        var parts = m.Name.Split(',', 2, StringSplitOptions.TrimEntries);
        return new ContactContent
        {
            LastName = parts.Length == 2 ? parts[0] : null,
            FirstName = parts.Length == 2 ? parts[1] : m.Name,
            Phones = string.IsNullOrWhiteSpace(m.Phone) ? Array.Empty<ContactPhone>() : new[] { new ContactPhone(m.Phone) },
            Emails = string.IsNullOrWhiteSpace(m.Email) ? Array.Empty<string>() : new[] { m.Email },
            Website = m.Url,
            Address = string.IsNullOrWhiteSpace(m.Address) ? null : new PostalAddress(Street: m.Address),
            Note = m.Note,
        };
    }

    private static string UtcStamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(";", "\\;", StringComparison.Ordinal)
                .Replace(",", "\\,", StringComparison.Ordinal)
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

    private static void Line(StringBuilder sb, string line) => sb.Append(line).Append(Crlf);
}
