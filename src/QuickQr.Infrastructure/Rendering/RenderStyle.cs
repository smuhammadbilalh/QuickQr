using System.Globalization;
using QuickQr.Core;
using QuickQr.Core.Rendering;
using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>A <see cref="QrStyle"/> with every colour parsed and every option normalised, ready for drawing.</summary>
internal sealed record RenderStyle
{
    public required SKColor Foreground { get; init; }
    public required SKColor Background { get; init; }
    public bool Transparent { get; init; }
    public GradientStyle? Gradient { get; init; }
    public SKColor GradientStart { get; init; }
    public SKColor GradientEnd { get; init; }
    public SKColor? EyeColor { get; init; }
    public ModuleShape ModuleShape { get; init; }
    public EyeFrameShape EyeFrameShape { get; init; }
    public EyeBallShape EyeBallShape { get; init; }
    public LogoOptions? Logo { get; init; }
    public FrameStyle Frame { get; init; }
    public string FrameText { get; init; } = string.Empty;
    public SKColor FrameColor { get; init; }
    public SKColor FrameTextColor { get; init; }
    public int QuietZoneModules { get; init; } = 4;
    public bool ShowHumanReadableText { get; init; } = true;

    public static Result<RenderStyle> Resolve(QrStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (!HexColor.TryParse(style.ForegroundColor, out var foreground)) return InvalidColour("foreground", style.ForegroundColor);
        if (!HexColor.TryParse(style.BackgroundColor, out var background)) return InvalidColour("background", style.BackgroundColor);

        SKColor? eye = null;
        if (style.EyeColor is not null)
        {
            if (!HexColor.TryParse(style.EyeColor, out var parsedEye)) return InvalidColour("eye", style.EyeColor);
            eye = parsedEye;
        }

        SKColor gradientStart = default, gradientEnd = default;
        if (style.Gradient is { } gradient)
        {
            if (!HexColor.TryParse(gradient.StartColor, out gradientStart)) return InvalidColour("gradient start", gradient.StartColor);
            if (!HexColor.TryParse(gradient.EndColor, out gradientEnd)) return InvalidColour("gradient end", gradient.EndColor);
            if (!float.IsFinite(gradient.AngleDegrees)) return Error.Validation("The gradient angle must be a number of degrees.");
        }

        var frameStyle = style.Frame?.Style ?? FrameStyle.None;
        SKColor frameColor = SKColors.Black, frameTextColor = SKColors.White;
        if (style.Frame is { } frame && frameStyle != FrameStyle.None)
        {
            if (!HexColor.TryParse(frame.FrameColor, out frameColor)) return InvalidColour("frame", frame.FrameColor);
            if (!HexColor.TryParse(frame.TextColor, out frameTextColor)) return InvalidColour("frame text", frame.TextColor);
        }

        if (style.QuietZoneModules is < 0 or > 40)
            return Error.Validation("The quiet zone must be between 0 and 40 modules (4 is the QR minimum).");

        return new RenderStyle
        {
            Foreground = foreground,
            Background = background,
            Transparent = style.TransparentBackground,
            Gradient = style.Gradient,
            GradientStart = gradientStart,
            GradientEnd = gradientEnd,
            EyeColor = eye,
            ModuleShape = Enum.IsDefined(style.ModuleShape) ? style.ModuleShape : ModuleShape.Square,
            EyeFrameShape = Enum.IsDefined(style.EyeFrameShape) ? style.EyeFrameShape : EyeFrameShape.Square,
            EyeBallShape = Enum.IsDefined(style.EyeBallShape) ? style.EyeBallShape : EyeBallShape.Square,
            Logo = style.Logo,
            Frame = Enum.IsDefined(frameStyle) ? frameStyle : FrameStyle.None,
            FrameText = style.Frame?.Text?.Trim() ?? string.Empty,
            FrameColor = frameColor,
            FrameTextColor = frameTextColor,
            QuietZoneModules = style.QuietZoneModules,
            ShowHumanReadableText = style.ShowHumanReadableText
        };
    }

    private static Error InvalidColour(string what, string? value) =>
        Error.Validation($"The {what} colour \"{value}\" isn't valid. Use a hex colour such as #000000, #FFF or #80FF0000.");
}

/// <summary>Parses #RGB, #RRGGBB and #AARRGGBB colours (the leading # is optional).</summary>
internal static class HexColor
{
    public static bool TryParse(string? value, out SKColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var hex = value.AsSpan().Trim();
        if (hex[0] == '#') hex = hex[1..];
        if (hex.Length is not (3 or 6 or 8)) return false;
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return false;

        color = hex.Length switch
        {
            3 => new SKColor((byte)(((v >> 8) & 0xF) * 17), (byte)(((v >> 4) & 0xF) * 17), (byte)((v & 0xF) * 17)),
            6 => new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v),
            _ => new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24))
        };
        return true;
    }
}
