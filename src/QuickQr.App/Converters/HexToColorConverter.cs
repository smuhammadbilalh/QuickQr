using System.Globalization;
using QuickQr.Controls;

namespace QuickQr.Converters;

/// <summary>Converts a hex string (#RRGGBB / #AARRGGBB) to a <see cref="Color"/>; invalid input becomes transparent.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        HexColor.TryParse(value as string, out var a, out var r, out var g, out var b)
            ? Color.FromRgba(r, g, b, a)
            : Colors.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color color ? color.ToArgbHex(includeAlpha: color.Alpha < 1f) : null;
}
