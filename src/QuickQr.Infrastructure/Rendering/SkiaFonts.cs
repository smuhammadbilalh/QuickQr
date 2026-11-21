using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>Process-wide typefaces (immutable and safe to share across threads) and SKFont helpers.</summary>
internal static class SkiaFonts
{
    private static readonly Lazy<SKTypeface> Regular = new(() => SKTypeface.Default);

    private static readonly Lazy<(SKTypeface Typeface, bool Synthetic)> Bold = new(() =>
    {
        var family = SKTypeface.Default.FamilyName;
        var match = SKFontManager.Default.MatchFamily(family, SKFontStyle.Bold)
            ?? SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
        return match is not null && match.FontWeight >= (int)SKFontStyleWeight.SemiBold
            ? (match, false)
            : (SKTypeface.Default, true);
    });

    public static SKFont Create(float size, bool bold = false)
    {
        var (typeface, synthetic) = bold ? Bold.Value : (Regular.Value, false);
        return new SKFont(typeface, Math.Max(size, 1f), 1f, 0f)
        {
            Embolden = synthetic,
            Edging = SKFontEdging.Antialias,
            Subpixel = true
        };
    }

    /// <summary>Shrinks the font so <paramref name="text"/> fits in <paramref name="maxWidth"/>.</summary>
    public static void FitWidth(SKFont font, string text, float maxWidth)
    {
        var width = font.MeasureText(text);
        if (width > maxWidth && width > 0) font.Size = Math.Max(1f, font.Size * maxWidth / width);
    }

    /// <summary>Truncates <paramref name="text"/> with an ellipsis so it fits in <paramref name="maxWidth"/>.</summary>
    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        const string ellipsis = "…";
        var available = Math.Max(0f, maxWidth - font.MeasureText(ellipsis));
        var count = font.BreakText(text, available);
        return count <= 0 ? ellipsis : string.Concat(text.AsSpan(0, count).TrimEnd(), ellipsis);
    }

    /// <summary>Baseline that vertically centres a single line of text in <paramref name="box"/>.</summary>
    public static float CenteredBaseline(SKFont font, SKRect box)
    {
        var metrics = font.Metrics;
        return box.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
    }
}
