using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>
/// Geometry of a rendered code in output units (pixels for raster/SVG, scaled to points for PDF).
/// Everything scales linearly with the requested code side.
/// </summary>
internal sealed class CodeLayout
{
    /// <summary>1D codes need a wider quiet zone than QR (ITF/Code 128 readers check for ~10 modules).</summary>
    public const int Min1DQuietZone = 10;

    public float Width { get; private init; }
    public float Height { get; private init; }

    /// <summary>Light area of the code itself, including the quiet zone.</summary>
    public SKRect CodeArea { get; private init; }

    /// <summary>The module grid (2D) or the bars (1D), excluding the quiet zone.</summary>
    public SKRect Modules { get; private init; }

    public float ModuleSize { get; private init; }
    public bool Is1D { get; private init; }

    /// <summary>Human-readable text box under 1D bars; empty when not shown.</summary>
    public SKRect TextArea { get; private init; }
    public float TextSize { get; private init; }

    public FrameStyle Frame { get; private init; }
    /// <summary>Outer bounds of the frame container (banner) or box (rounded box).</summary>
    public SKRect FrameBounds { get; private init; }
    /// <summary>Band holding the call-to-action text; empty when there is none.</summary>
    public SKRect BandArea { get; private init; }
    public float FrameRadius { get; private init; }
    public float FrameThickness { get; private init; }

    public static CodeLayout Compute(BarcodeMatrix matrix, RenderStyle style, float side)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(style);

        var is1D = matrix.Height == 1;
        var quiet = is1D ? Math.Max(style.QuietZoneModules, Min1DQuietZone) : style.QuietZoneModules;
        var module = side / (matrix.Width + (2f * quiet));

        float codeHeight;
        SKRect modules, text = SKRect.Empty;
        float textSize = 0;
        if (!is1D)
        {
            codeHeight = module * (matrix.Height + (2f * quiet));
            modules = SKRect.Create(quiet * module, quiet * module, matrix.Width * module, matrix.Height * module);
        }
        else
        {
            var padding = side * 0.05f;
            var barHeight = side * 0.4f;
            modules = SKRect.Create(quiet * module, padding, matrix.Width * module, barHeight);
            codeHeight = padding + barHeight + padding;
            if (style.ShowHumanReadableText && !string.IsNullOrWhiteSpace(matrix.Payload))
            {
                textSize = side * 0.07f;
                var gap = textSize * 0.25f;
                text = SKRect.Create(quiet * module, padding + barHeight + gap, side - (2f * quiet * module), textSize * 1.25f);
                codeHeight += gap + text.Height;
            }
        }

        var thickness = side * 0.04f;
        var radius = side * 0.06f;
        var hasText = style.FrameText.Length > 0;
        float dx = 0, dy = 0, width = side, height = codeHeight;
        SKRect frameBounds = SKRect.Empty, band = SKRect.Empty;

        switch (style.Frame)
        {
            case FrameStyle.BannerBottom:
            {
                var bandHeight = side * 0.18f;
                width = side + (2f * thickness);
                height = thickness + codeHeight + bandHeight;
                (dx, dy) = (thickness, thickness);
                frameBounds = SKRect.Create(width, height);
                band = new SKRect(thickness, thickness + codeHeight, width - thickness, height);
                break;
            }
            case FrameStyle.BannerTop:
            {
                var bandHeight = side * 0.18f;
                width = side + (2f * thickness);
                height = bandHeight + codeHeight + thickness;
                (dx, dy) = (thickness, bandHeight);
                frameBounds = SKRect.Create(width, height);
                band = new SKRect(thickness, 0, width - thickness, bandHeight);
                break;
            }
            case FrameStyle.RoundedBox:
            {
                var bandHeight = hasText ? side * 0.14f : 0f;
                width = side + (2f * thickness);
                height = codeHeight + (2f * thickness) + bandHeight;
                (dx, dy) = (thickness, thickness);
                frameBounds = SKRect.Create(width, codeHeight + (2f * thickness));
                band = hasText ? new SKRect(0, frameBounds.Bottom, width, height) : SKRect.Empty;
                break;
            }
        }

        return new CodeLayout
        {
            Width = width,
            Height = height,
            CodeArea = SKRect.Create(dx, dy, side, codeHeight),
            Modules = Offset(modules, dx, dy),
            ModuleSize = module,
            Is1D = is1D,
            TextArea = text.IsEmpty ? SKRect.Empty : Offset(text, dx, dy),
            TextSize = textSize,
            Frame = style.Frame,
            FrameBounds = frameBounds,
            BandArea = band,
            FrameRadius = radius,
            FrameThickness = thickness
        };
    }

    /// <summary>Largest layout whose outer size fits in <paramref name="maxWidth"/> × <paramref name="maxHeight"/>.</summary>
    public static CodeLayout Fit(BarcodeMatrix matrix, RenderStyle style, float maxWidth, float maxHeight)
    {
        var unit = Compute(matrix, style, 100f);
        var scale = Math.Min(maxWidth / unit.Width, maxHeight / unit.Height);
        return Compute(matrix, style, Math.Max(0.01f, 100f * scale));
    }

    private static SKRect Offset(SKRect rect, float dx, float dy) =>
        new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);
}
