using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>iCalendar (RFC 5545) events: a bare <c>VEVENT</c> for QR payloads and a full <c>VCALENDAR</c> for calendar import.</summary>
internal static partial class CalendarFormat
{
    public const string BeginEvent = "BEGIN:VEVENT";
    public const string BeginCalendar = "BEGIN:VCALENDAR";

    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string DateFormat = "yyyyMMdd";

    [GeneratedRegex(@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    public static string Write(CalendarEventContent content) => string.Join("\r\n", EventLines(content, identity: null));

    /// <summary>A complete, importable .ics document (CRLF, folded lines, UID and DTSTAMP).</summary>
    public static string WriteCalendar(CalendarEventContent content, DateTimeOffset stamp)
    {
        var eventText = Write(content);
        var uid = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(eventText)).AsSpan(0, 16)) + "@quickqr";
        var identity = new[] { "UID:" + uid, "DTSTAMP:" + stamp.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture) };

        var sb = new StringBuilder();
        foreach (var line in new[] { BeginCalendar, "VERSION:2.0", "PRODID:-//QuickQr//EN" }
                     .Concat(EventLines(content, identity))
                     .Append("END:VCALENDAR"))
        {
            ContentLines.AppendFolded(sb, line);
        }
        return sb.ToString();
    }

    private static List<string> EventLines(CalendarEventContent content, string[]? identity)
    {
        var lines = new List<string> { BeginEvent };
        if (identity is not null) lines.AddRange(identity);
        if (PayloadText.Clean(content.Title) is { } title) lines.Add("SUMMARY:" + PayloadText.EscapeText(title));

        if (content.AllDay)
        {
            var start = DateOnly.FromDateTime(content.Start.DateTime);
            var lastDay = DateOnly.FromDateTime(content.End.DateTime);
            if (lastDay < start) lastDay = start;
            lines.Add("DTSTART;VALUE=DATE:" + start.ToString(DateFormat, CultureInfo.InvariantCulture));
            // DTEND is exclusive for all-day events: the day after the last day.
            lines.Add("DTEND;VALUE=DATE:" + lastDay.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture));
        }
        else
        {
            var end = content.End < content.Start ? content.Start : content.End;
            lines.Add("DTSTART:" + content.Start.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture));
            lines.Add("DTEND:" + end.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture));
        }

        if (PayloadText.Clean(content.Location) is { } location) lines.Add("LOCATION:" + PayloadText.EscapeText(location));
        if (PayloadText.Clean(content.Description) is { } description) lines.Add("DESCRIPTION:" + PayloadText.EscapeText(description));
        lines.Add("END:VEVENT");
        return lines;
    }

    public static Error? Validate(CalendarEventContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Title))
            return Error.Validation("Give the event a title.");
        var endsBeforeStart = content.AllDay
            ? DateOnly.FromDateTime(content.End.DateTime) < DateOnly.FromDateTime(content.Start.DateTime)
            : content.End < content.Start;
        return endsBeforeStart ? Error.Validation("The event can't end before it starts.") : null;
    }

    public static QrContent? TryParse(string payload)
    {
        var lines = ContentLines.Unfold(payload);
        var begin = lines.FindIndex(l => l.Trim().Equals(BeginEvent, StringComparison.OrdinalIgnoreCase));
        if (begin < 0) return null;

        string? title = null, location = null, description = null;
        ParsedTime? start = null, end = null;
        TimeSpan? duration = null;

        for (var i = begin + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase)) break;
            if (ContentLines.Parse(lines[i]) is not { } line) continue;
            switch (line.Name)
            {
                case "SUMMARY": title ??= PayloadText.UnescapeText(line.Value).Trim(); break;
                case "LOCATION": location ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value)); break;
                case "DESCRIPTION": description ??= PayloadText.Clean(PayloadText.UnescapeText(line.Value)); break;
                case "DTSTART": start ??= ParseTime(line); break;
                case "DTEND": end ??= ParseTime(line); break;
                case "DURATION": duration ??= ParseDuration(line.Value.Trim()); break;
            }
        }

        if (start is not { } s) return null;

        if (s.IsDate)
        {
            var lastDay = s.Value;
            if (end is { } e && e.Value > s.Value) lastDay = e.Value.AddDays(-1);
            else if (duration is { } d && d >= TimeSpan.FromDays(1)) lastDay = s.Value.AddDays(Math.Ceiling(d.TotalDays) - 1);
            return new CalendarEventContent(title ?? string.Empty, s.Value, lastDay, AllDay: true, location, description);
        }

        var endValue = end?.Value ?? (duration is { } dur ? s.Value + dur : s.Value);
        if (endValue < s.Value) endValue = s.Value;
        return new CalendarEventContent(title ?? string.Empty, s.Value, endValue, AllDay: false, location, description);
    }

    private readonly record struct ParsedTime(DateTimeOffset Value, bool IsDate);

    private static ParsedTime? ParseTime(ContentLine line)
    {
        var value = line.Value.Trim();
        var isDate = string.Equals(line.Parameter("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;

        if (isDate)
        {
            return DateTime.TryParseExact(value.Length >= 8 ? value[..8] : value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? new ParsedTime(new DateTimeOffset(date, TimeSpan.Zero), true)
                : null;
        }

        if (value.EndsWith('Z') || value.EndsWith('z'))
        {
            return DateTime.TryParseExact(value[..^1], ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var utc)
                ? new ParsedTime(new DateTimeOffset(utc, TimeSpan.Zero), false)
                : null;
        }

        if (!DateTime.TryParseExact(value, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        try
        {
            var zone = TimeZoneInfo.Local;
            if (line.Parameter("TZID") is { Length: > 0 } tzid &&
                TimeZoneInfo.TryFindSystemTimeZoneById(tzid.Trim('"'), out var found))
            {
                zone = found;
            }
            return new ParsedTime(new DateTimeOffset(local, zone.GetUtcOffset(local)), false);
        }
        catch (ArgumentException)
        {
            return new ParsedTime(new DateTimeOffset(local, TimeSpan.Zero), false);
        }
    }

    private static TimeSpan? ParseDuration(string value)
    {
        var match = DurationRegex().Match(value);
        if (!match.Success) return null;
        static int Part(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;
        try
        {
            var span = TimeSpan.FromDays((Part(match.Groups[2]) * 7) + Part(match.Groups[3])) +
                       new TimeSpan(Part(match.Groups[4]), Part(match.Groups[5]), Part(match.Groups[6]));
            return match.Groups[1].Value == "-" ? span.Negate() : span;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Human-readable date range in the device's local time zone (invariant formatting).</summary>
    public static string DescribeRange(CalendarEventContent content)
    {
        if (content.AllDay)
        {
            var start = DateOnly.FromDateTime(content.Start.DateTime);
            var last = DateOnly.FromDateTime(content.End.DateTime);
            var first = start.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            return last <= start ? first + " (all day)" : $"{first} – {last.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        var localStart = content.Start.ToLocalTime();
        var localEnd = (content.End < content.Start ? content.Start : content.End).ToLocalTime();
        var startText = localStart.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        if (localEnd == localStart) return startText;
        return localEnd.Date == localStart.Date
            ? $"{startText} – {localEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : $"{startText} – {localEnd.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}";
    }
}
