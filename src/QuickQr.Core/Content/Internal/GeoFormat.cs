using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Geographic location: RFC 5870 <c>geo:lat,lon[,alt]</c> with the Android <c>?q=</c> extension, and bare "lat,lon".</summary>
internal static partial class GeoFormat
{
    public const string Prefix = "geo:";

    [GeneratedRegex(@"^\s*([-+]?\d{1,3}\.\d+)\s*,\s*([-+]?\d{1,3}\.\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainCoordinates();

    [GeneratedRegex(@"^\s*([-+]?\d{1,3}(?:\.\d+)?)\s*,\s*([-+]?\d{1,3}(?:\.\d+)?)\s*(?:\((.*)\))?\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex QueryCoordinates();

    public static string Write(GeoContent content)
    {
        var text = $"{Prefix}{Format(content.Latitude)},{Format(content.Longitude)}";
        if (content.Altitude is { } altitude && double.IsFinite(altitude)) text += "," + Format(altitude);
        if (PayloadText.Clean(content.Query) is { } query) text += "?q=" + PayloadText.PercentEncode(query);
        return text;
    }

    /// <summary>"lat,lon" with invariant culture (used by map actions).</summary>
    public static string Coordinates(GeoContent content) => $"{Format(content.Latitude)},{Format(content.Longitude)}";

    public static string Format(double value)
    {
        if (value == 0 || !double.IsFinite(value)) return "0";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('E', StringComparison.OrdinalIgnoreCase)
            ? value.ToString("0.###############", CultureInfo.InvariantCulture)
            : text;
    }

    public static Error? Validate(GeoContent content)
    {
        if (!double.IsFinite(content.Latitude) || content.Latitude is < -90 or > 90)
            return Error.Validation("Latitude must be between -90 and 90.");
        if (!double.IsFinite(content.Longitude) || content.Longitude is < -180 or > 180)
            return Error.Validation("Longitude must be between -180 and 180.");
        if (content.Altitude is { } altitude && !double.IsFinite(altitude))
            return Error.Validation("Enter a valid altitude.");
        return null;
    }

    public static QrContent? TryParse(string payload)
    {
        var rest = payload[Prefix.Length..];
        if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];

        var questionMark = rest.IndexOf('?', StringComparison.Ordinal);
        var coordinates = questionMark < 0 ? rest : rest[..questionMark];
        var query = questionMark < 0 ? [] : PayloadText.ParseQuery(rest[(questionMark + 1)..], plusAsSpace: true);
        var q = PayloadText.Clean(PayloadText.GetValue(query, "q"));

        var parameters = coordinates.IndexOf(';', StringComparison.Ordinal); // ;crs=wgs84;u=35
        if (parameters >= 0) coordinates = coordinates[..parameters];

        var parts = coordinates.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !TryParseNumber(parts[0], out var latitude) || !TryParseNumber(parts[1], out var longitude))
        {
            return q is null ? null : new GeoContent(0, 0, null, q);
        }

        double? altitude = parts.Length >= 3 && TryParseNumber(parts[2], out var alt) ? alt : null;

        // Android: geo:0,0?q=48.85,2.29(Eiffel Tower)
        if (latitude == 0 && longitude == 0 && q is not null && QueryCoordinates().Match(q) is { Success: true } match &&
            TryParseNumber(match.Groups[1].Value, out var qLat) && TryParseNumber(match.Groups[2].Value, out var qLon) && InRange(qLat, qLon))
        {
            latitude = qLat;
            longitude = qLon;
            q = PayloadText.Clean(match.Groups[3].Value);
        }

        return InRange(latitude, longitude) ? new GeoContent(latitude, longitude, altitude, q) : null;
    }

    public static QrContent? TryParsePlain(string text)
    {
        var match = PlainCoordinates().Match(text);
        if (!match.Success) return null;
        return TryParseNumber(match.Groups[1].Value, out var latitude) && TryParseNumber(match.Groups[2].Value, out var longitude) &&
               InRange(latitude, longitude)
            ? new GeoContent(latitude, longitude)
            : null;
    }

    private static bool InRange(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static bool TryParseNumber(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
}
