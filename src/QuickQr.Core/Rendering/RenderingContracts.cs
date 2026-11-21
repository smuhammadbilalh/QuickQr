using QuickQr.Core.Barcodes;

namespace QuickQr.Core.Rendering;

public enum ModuleShape { Square = 0, Rounded = 1, Circle = 2, Diamond = 3, VerticalBars = 4, HorizontalBars = 5 }
public enum EyeFrameShape { Square = 0, Rounded = 1, Circle = 2 }
public enum EyeBallShape { Square = 0, Rounded = 1, Circle = 2, Diamond = 3 }
public enum GradientType { Linear = 0, Radial = 1 }
public enum LogoBackgroundShape { None = 0, Square = 1, Rounded = 2, Circle = 3 }
public enum FrameStyle { None = 0, BannerBottom = 1, BannerTop = 2, RoundedBox = 3 }

/// <summary>Colours are hex strings (#RRGGBB or #AARRGGBB) so styles stay serialisable and UI-agnostic.</summary>
public sealed record GradientStyle(GradientType Type, string StartColor, string EndColor, float AngleDegrees = 45f);

/// <param name="ImageBytes">PNG/JPEG/WebP/SVG bytes of the logo.</param>
/// <param name="SizeRatio">Logo width relative to the code width (0.10–0.30). Above ~0.25 requires High error correction.</param>
/// <param name="PaddingRatio">Clear margin around the logo relative to the logo size.</param>
public sealed record LogoOptions(
    byte[] ImageBytes,
    float SizeRatio = 0.22f,
    float PaddingRatio = 0.08f,
    LogoBackgroundShape Background = LogoBackgroundShape.Rounded);

public sealed record FrameOptions(
    FrameStyle Style = FrameStyle.BannerBottom,
    string Text = "SCAN ME",
    string FrameColor = "#000000",
    string TextColor = "#FFFFFF");

/// <summary>Visual design of a code. The default instance renders a classic black-on-white code (free tier).</summary>
public sealed record QrStyle
{
    public static QrStyle Default { get; } = new();

    public string ForegroundColor { get; init; } = "#000000";
    public string BackgroundColor { get; init; } = "#FFFFFF";
    public bool TransparentBackground { get; init; }
    public GradientStyle? Gradient { get; init; }
    public ModuleShape ModuleShape { get; init; } = ModuleShape.Square;
    public EyeFrameShape EyeFrameShape { get; init; } = EyeFrameShape.Square;
    public EyeBallShape EyeBallShape { get; init; } = EyeBallShape.Square;
    /// <summary>Colour of the finder patterns; null uses <see cref="ForegroundColor"/>.</summary>
    public string? EyeColor { get; init; }
    public LogoOptions? Logo { get; init; }
    public FrameOptions? Frame { get; init; }
    /// <summary>Quiet zone in modules (QR spec minimum is 4).</summary>
    public int QuietZoneModules { get; init; } = 4;
    /// <summary>Print the payload under 1D barcodes.</summary>
    public bool ShowHumanReadableText { get; init; } = true;
}

public enum ExportFormat { Png = 0, Jpeg = 1, Webp = 2, Svg = 3, Pdf = 4 }

/// <param name="SizePixels">Edge length of the code area in pixels for raster formats (frames add to it).</param>
/// <param name="Dpi">Embedded resolution for raster/PDF output (print workflows).</param>
public sealed record RenderOptions(
    int SizePixels = 1024,
    ExportFormat Format = ExportFormat.Png,
    int Dpi = 300,
    int Quality = 95);

public sealed record RenderedImage(byte[] Data, ExportFormat Format, int Width, int Height)
{
    public string MimeType => Format switch
    {
        ExportFormat.Png => "image/png",
        ExportFormat.Jpeg => "image/jpeg",
        ExportFormat.Webp => "image/webp",
        ExportFormat.Svg => "image/svg+xml",
        ExportFormat.Pdf => "application/pdf",
        _ => "application/octet-stream"
    };

    public string FileExtension => Format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Webp => ".webp",
        ExportFormat.Svg => ".svg",
        ExportFormat.Pdf => ".pdf",
        _ => ".bin"
    };
}

/// <summary>Renders a <see cref="BarcodeMatrix"/> with a <see cref="QrStyle"/> into an exportable image.</summary>
public interface IBarcodeRenderer
{
    Task<Result<RenderedImage>> RenderAsync(
        BarcodeMatrix matrix,
        QrStyle style,
        RenderOptions options,
        CancellationToken cancellationToken = default);
}
