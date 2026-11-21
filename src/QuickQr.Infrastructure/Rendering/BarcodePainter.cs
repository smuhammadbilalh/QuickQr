using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>
/// The single drawing routine shared by raster, PDF, SVG and print-sheet output. Draws in canvas units
/// (the caller transforms the canvas). Stateless: safe to call from any thread with its own canvas.
/// </summary>
internal static class BarcodePainter
{
    /// <summary>
    /// Cubic Bézier handle length for a quarter circle. Curves are built from cubics rather than conics because Skia's
    /// SVG and PDF back ends flatten every conic into dozens of quads (megabyte-sized SVGs).
    /// </summary>
    private const float Kappa = 0.5522847498f;

    private const int FinderSize = 7;

    public static void DrawCode(SKCanvas canvas, BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKImage? logo)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(layout);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        if (!style.Transparent)
        {
            fill.Color = style.Background;
            canvas.DrawRect(SKRect.Create(layout.Width, layout.Height), fill);
        }

        DrawFrame(canvas, style, layout, fill);

        using var foreground = CreateForegroundPaint(style, layout.Modules);

        if (layout.Is1D)
        {
            DrawBars(canvas, matrix, layout, foreground);
            DrawHumanReadable(canvas, matrix, style, layout);
            return;
        }

        var advanced = matrix.Format.SupportsAdvancedStyling();
        var plate = advanced && logo is not null ? LogoPlate.Compute(matrix, style, layout) : null;

        DrawModules(canvas, matrix, style, layout, foreground, advanced, plate);

        if (advanced)
        {
            if (style.EyeColor is { } eyeColor)
            {
                using var eyePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = eyeColor };
                DrawEyes(canvas, matrix, style, layout, eyePaint);
            }
            else
            {
                DrawEyes(canvas, matrix, style, layout, foreground);
            }
        }

        if (plate is not null && logo is not null)
            DrawLogo(canvas, style, plate, logo, fill);
    }

    /// <summary>A neutral stand-in used on print sheets when an item could not be encoded.</summary>
    public static void DrawPlaceholder(SKCanvas canvas, SKRect box, string message)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var side = Math.Min(box.Width, box.Height);
        var rect = SKRect.Create(box.MidX - (side / 2f), box.MidY - (side / 2f), side, side);
        var radius = side * 0.06f;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xF1, 0xF1, 0xF1) };
        canvas.DrawRoundRect(rect, radius, radius, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Math.Max(0.5f, side * 0.01f);
        paint.Color = new SKColor(0xB0, 0xB0, 0xB0);
        using var dash = SKPathEffect.CreateDash([side * 0.04f, side * 0.03f], 0);
        paint.PathEffect = dash;
        canvas.DrawRoundRect(rect, radius, radius, paint);

        paint.PathEffect = null;
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(0x70, 0x70, 0x70);
        using var font = SkiaFonts.Create(side * 0.09f, bold: true);
        SkiaFonts.FitWidth(font, message, side * 0.85f);
        canvas.DrawText(message, rect.MidX, SkiaFonts.CenteredBaseline(font, rect), SKTextAlign.Center, font, paint);
    }

    private static SKPaint CreateForegroundPaint(RenderStyle style, SKRect area)
    {
        var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = style.Foreground };
        if (style.Gradient is { } gradient)
        {
            SKColor[] colors = [style.GradientStart, style.GradientEnd];
            var center = new SKPoint(area.MidX, area.MidY);
            if (gradient.Type == GradientType.Radial)
            {
                var radius = MathF.Max(1f, MathF.Sqrt((area.Width * area.Width) + (area.Height * area.Height)) / 2f);
                paint.Shader = SKShader.CreateRadialGradient(center, radius, colors, SKShaderTileMode.Clamp);
            }
            else
            {
                var angle = gradient.AngleDegrees * MathF.PI / 180f;
                var direction = new SKPoint(MathF.Cos(angle), MathF.Sin(angle));
                var half = MathF.Max(0.5f, (MathF.Abs(direction.X) * area.Width / 2f) + (MathF.Abs(direction.Y) * area.Height / 2f));
                var start = new SKPoint(center.X - (direction.X * half), center.Y - (direction.Y * half));
                var end = new SKPoint(center.X + (direction.X * half), center.Y + (direction.Y * half));
                paint.Shader = SKShader.CreateLinearGradient(start, end, colors, SKShaderTileMode.Clamp);
            }
        }
        return paint;
    }

    // ---------------------------------------------------------------- frames

    private static void DrawFrame(SKCanvas canvas, RenderStyle style, CodeLayout layout, SKPaint fill)
    {
        if (layout.Frame == FrameStyle.None) return;

        var radius = layout.FrameRadius;
        var innerRadius = Math.Max(0f, radius - layout.FrameThickness);

        // Container (or box) with the code area punched out so a transparent background stays transparent.
        using (var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd })
        {
            AddRoundedRect(builder, layout.FrameBounds, radius);
            AddRoundedRect(builder, layout.CodeArea, innerRadius);
            using var path = builder.Detach();
            fill.Color = style.FrameColor;
            canvas.DrawPath(path, fill);
        }

        if (layout.BandArea.IsEmpty || style.FrameText.Length == 0) return;

        var band = layout.BandArea;
        fill.Color = layout.Frame == FrameStyle.RoundedBox ? style.FrameColor : style.FrameTextColor;
        using var font = SkiaFonts.Create(band.Height * (layout.Frame == FrameStyle.RoundedBox ? 0.55f : 0.45f), bold: true);
        SkiaFonts.FitWidth(font, style.FrameText, band.Width * 0.9f);
        canvas.DrawText(style.FrameText, band.MidX, SkiaFonts.CenteredBaseline(font, band), SKTextAlign.Center, font, fill);
    }

    // ---------------------------------------------------------------- 1D

    private static void DrawBars(SKCanvas canvas, BarcodeMatrix matrix, CodeLayout layout, SKPaint paint)
    {
        var m = layout.ModuleSize;
        var bars = layout.Modules;
        using var builder = new SKPathBuilder();
        for (var x = 0; x < matrix.Width;)
        {
            if (!matrix[x, 0])
            {
                x++;
                continue;
            }
            var start = x;
            while (x < matrix.Width && matrix[x, 0]) x++;
            builder.AddRect(new SKRect(bars.Left + (start * m), bars.Top, bars.Left + (x * m), bars.Bottom));
        }
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static void DrawHumanReadable(SKCanvas canvas, BarcodeMatrix matrix, RenderStyle style, CodeLayout layout)
    {
        if (layout.TextArea.IsEmpty) return;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = style.Foreground };
        using var font = SkiaFonts.Create(layout.TextSize);
        SkiaFonts.FitWidth(font, matrix.Payload, layout.TextArea.Width);
        canvas.DrawText(matrix.Payload, layout.TextArea.MidX, SkiaFonts.CenteredBaseline(font, layout.TextArea), SKTextAlign.Center, font, paint);
    }

    // ---------------------------------------------------------------- 2D modules

    private static void DrawModules(SKCanvas canvas, BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKPaint paint, bool advanced, LogoPlate? plate)
    {
        var m = layout.ModuleSize;
        var origin = new SKPoint(layout.Modules.Left, layout.Modules.Top);
        var width = matrix.Width;
        var height = matrix.Height;

        // Which modules this pass draws: finder patterns are drawn as eyes, modules under the logo plate are skipped.
        var dark = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                dark[(y * width) + x] = matrix[x, y]
                    && !(advanced && matrix.IsFinderPattern(x, y))
                    && !(plate?.Covers(origin, m, x, y) ?? false);
            }
        }

        bool Dark(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && dark[(y * width) + x];

        var shape = advanced ? style.ModuleShape : ModuleShape.Square;
        using var builder = new SKPathBuilder();
        switch (shape)
        {
            case ModuleShape.Rounded:
            {
                var r = m / 2f;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (!Dark(x, y)) continue;
                        bool up = Dark(x, y - 1), down = Dark(x, y + 1), left = Dark(x - 1, y), right = Dark(x + 1, y);
                        AddRoundedRect(builder, Cell(origin, m, x, y),
                            !up && !left ? r : 0, !up && !right ? r : 0, !down && !right ? r : 0, !down && !left ? r : 0);
                    }
                }
                break;
            }
            case ModuleShape.Circle:
            {
                var r = m * 0.46f;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (Dark(x, y)) AddCircle(builder, origin.X + ((x + 0.5f) * m), origin.Y + ((y + 0.5f) * m), r);
                    }
                }
                break;
            }
            case ModuleShape.Diamond:
            {
                var h = m * 0.55f; // a touch larger than the cell's half-diagonal so neighbours join
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (!Dark(x, y)) continue;
                        float cx = origin.X + ((x + 0.5f) * m), cy = origin.Y + ((y + 0.5f) * m);
                        AddDiamond(builder, cx, cy, h);
                    }
                }
                break;
            }
            case ModuleShape.VerticalBars:
            {
                var inset = m * 0.1f;
                for (var x = 0; x < width; x++)
                {
                    for (var y = 0; y < height;)
                    {
                        if (!Dark(x, y)) { y++; continue; }
                        var start = y;
                        while (Dark(x, y)) y++;
                        var rect = new SKRect(origin.X + (x * m) + inset, origin.Y + (start * m) + inset, origin.X + ((x + 1) * m) - inset, origin.Y + (y * m) - inset);
                        var radius = rect.Width / 2f;
                        AddRoundedRect(builder, rect, radius);
                    }
                }
                break;
            }
            case ModuleShape.HorizontalBars:
            {
                var inset = m * 0.1f;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width;)
                    {
                        if (!Dark(x, y)) { x++; continue; }
                        var start = x;
                        while (Dark(x, y)) x++;
                        var rect = new SKRect(origin.X + (start * m) + inset, origin.Y + (y * m) + inset, origin.X + (x * m) - inset, origin.Y + ((y + 1) * m) - inset);
                        var radius = rect.Height / 2f;
                        AddRoundedRect(builder, rect, radius);
                    }
                }
                break;
            }
            default:
            {
                // Merge horizontal runs, and identical consecutive rows (PDF417 rows are several modules tall):
                // fewer path elements and no anti-aliasing seams between neighbours.
                for (var y = 0; y < height;)
                {
                    var rows = 1;
                    while (y + rows < height && dark.AsSpan((y + rows) * width, width).SequenceEqual(dark.AsSpan(y * width, width))) rows++;

                    for (var x = 0; x < width;)
                    {
                        if (!Dark(x, y)) { x++; continue; }
                        var start = x;
                        while (Dark(x, y)) x++;
                        builder.AddRect(new SKRect(origin.X + (start * m), origin.Y + (y * m), origin.X + (x * m), origin.Y + ((y + rows) * m)));
                    }
                    y += rows;
                }
                break;
            }
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static SKRect Cell(SKPoint origin, float m, int x, int y) =>
        new(origin.X + (x * m), origin.Y + (y * m), origin.X + ((x + 1) * m), origin.Y + ((y + 1) * m));

    private static void AddRoundedRect(SKPathBuilder b, SKRect r, float radius) => AddRoundedRect(b, r, radius, radius, radius, radius);

    /// <summary>Clockwise rectangle with individually rounded corners (upper-left, upper-right, lower-right, lower-left).</summary>
    private static void AddRoundedRect(SKPathBuilder b, SKRect r, float ul, float ur, float lr, float ll)
    {
        var max = Math.Min(r.Width, r.Height) / 2f;
        ul = Math.Clamp(ul, 0, max);
        ur = Math.Clamp(ur, 0, max);
        lr = Math.Clamp(lr, 0, max);
        ll = Math.Clamp(ll, 0, max);

        b.MoveTo(r.Left + ul, r.Top);
        b.LineTo(r.Right - ur, r.Top);
        if (ur > 0) b.CubicTo(r.Right - ur + (Kappa * ur), r.Top, r.Right, r.Top + ur - (Kappa * ur), r.Right, r.Top + ur);
        b.LineTo(r.Right, r.Bottom - lr);
        if (lr > 0) b.CubicTo(r.Right, r.Bottom - lr + (Kappa * lr), r.Right - lr + (Kappa * lr), r.Bottom, r.Right - lr, r.Bottom);
        b.LineTo(r.Left + ll, r.Bottom);
        if (ll > 0) b.CubicTo(r.Left + ll - (Kappa * ll), r.Bottom, r.Left, r.Bottom - ll + (Kappa * ll), r.Left, r.Bottom - ll);
        b.LineTo(r.Left, r.Top + ul);
        if (ul > 0) b.CubicTo(r.Left, r.Top + ul - (Kappa * ul), r.Left + ul - (Kappa * ul), r.Top, r.Left + ul, r.Top);
        b.Close();
    }

    /// <summary>Clockwise circle from four cubic arcs.</summary>
    private static void AddCircle(SKPathBuilder b, float cx, float cy, float r)
    {
        var k = Kappa * r;
        b.MoveTo(cx + r, cy);
        b.CubicTo(cx + r, cy + k, cx + k, cy + r, cx, cy + r);
        b.CubicTo(cx - k, cy + r, cx - r, cy + k, cx - r, cy);
        b.CubicTo(cx - r, cy - k, cx - k, cy - r, cx, cy - r);
        b.CubicTo(cx + k, cy - r, cx + r, cy - k, cx + r, cy);
        b.Close();
    }

    private static void AddDiamond(SKPathBuilder b, float cx, float cy, float half)
    {
        b.MoveTo(cx, cy - half);
        b.LineTo(cx + half, cy);
        b.LineTo(cx, cy + half);
        b.LineTo(cx - half, cy);
        b.Close();
    }

    // ---------------------------------------------------------------- eyes

    private static void DrawEyes(SKCanvas canvas, BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKPaint paint)
    {
        if (matrix.Width < FinderSize * 2 || matrix.Height < FinderSize * 2) return;

        var m = layout.ModuleSize;
        (int X, int Y)[] corners = [(0, 0), (matrix.Width - FinderSize, 0), (0, matrix.Height - FinderSize)];

        using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        foreach (var (cx, cy) in corners)
        {
            var outer = SKRect.Create(layout.Modules.Left + (cx * m), layout.Modules.Top + (cy * m), FinderSize * m, FinderSize * m);
            var inner = Inset(outer, m);
            var ball = Inset(outer, 2 * m);

            switch (style.EyeFrameShape)
            {
                case EyeFrameShape.Rounded:
                    AddRoundedRect(builder, outer, 2f * m);
                    AddRoundedRect(builder, inner, m);
                    break;
                case EyeFrameShape.Circle:
                    AddCircle(builder, outer.MidX, outer.MidY, 3.5f * m);
                    AddCircle(builder, outer.MidX, outer.MidY, 2.5f * m);
                    break;
                default:
                    builder.AddRect(outer);
                    builder.AddRect(inner);
                    break;
            }

            switch (style.EyeBallShape)
            {
                case EyeBallShape.Rounded:
                    AddRoundedRect(builder, ball, 0.8f * m);
                    break;
                case EyeBallShape.Circle:
                    AddCircle(builder, ball.MidX, ball.MidY, 1.5f * m);
                    break;
                case EyeBallShape.Diamond:
                    // Slightly oversized so scanners' diagonal finder check still sees ~1:1:3:1:1.
                    AddDiamond(builder, ball.MidX, ball.MidY, 1.875f * m);
                    break;
                default:
                    builder.AddRect(ball);
                    break;
            }
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static SKRect Inset(SKRect rect, float by) => new(rect.Left + by, rect.Top + by, rect.Right - by, rect.Bottom - by);

    // ---------------------------------------------------------------- logo

    private static void DrawLogo(SKCanvas canvas, RenderStyle style, LogoPlate plate, SKImage logo, SKPaint fill)
    {
        if (plate.Shape != LogoBackgroundShape.None && !style.Transparent)
        {
            fill.Color = style.Background;
            fill.Shader = null;
            switch (plate.Shape)
            {
                case LogoBackgroundShape.Circle:
                    canvas.DrawCircle(plate.Bounds.MidX, plate.Bounds.MidY, plate.Bounds.Width / 2f, fill);
                    break;
                case LogoBackgroundShape.Rounded:
                    canvas.DrawRoundRect(plate.Bounds, plate.CornerRadius, plate.CornerRadius, fill);
                    break;
                default:
                    canvas.DrawRect(plate.Bounds, fill);
                    break;
            }
        }

        // Fit (contain) the logo into its box, preserving aspect ratio.
        var box = plate.LogoBox;
        var scale = Math.Min(box.Width / logo.Width, box.Height / logo.Height);
        var w = logo.Width * scale;
        var h = logo.Height * scale;
        var dest = SKRect.Create(box.MidX - (w / 2f), box.MidY - (h / 2f), w, h);
        using var imagePaint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(logo, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), imagePaint);
    }

    /// <summary>Where the logo goes and which modules it hides.</summary>
    private sealed class LogoPlate
    {
        public required SKRect Bounds { get; init; }
        public required SKRect LogoBox { get; init; }
        public required LogoBackgroundShape Shape { get; init; }
        public required float CornerRadius { get; init; }

        public static LogoPlate Compute(BarcodeMatrix matrix, RenderStyle style, CodeLayout layout)
        {
            var options = style.Logo!;
            var m = layout.ModuleSize;
            var codeWidth = matrix.Width * m;
            var shape = Enum.IsDefined(options.Background) ? options.Background : LogoBackgroundShape.Rounded;

            var ratio = Math.Clamp(float.IsFinite(options.SizeRatio) ? options.SizeRatio : 0.22f, 0.10f, 0.30f);
            var paddingRatio = Math.Clamp(float.IsFinite(options.PaddingRatio) ? options.PaddingRatio : 0.08f, 0f, 0.5f);
            var logoSize = codeWidth * ratio;
            var padding = logoSize * paddingRatio;

            // A circle needs a little more room for a square logo to look the same size.
            var innerSize = shape == LogoBackgroundShape.Circle ? logoSize * 1.2f : logoSize;
            var plateSize = innerSize + (2f * padding);

            // Never cover the finder patterns and their separators, whatever the version.
            var maxPlate = (matrix.Width - (2 * (FinderSize + 1))) * m;
            if (maxPlate > 0 && plateSize > maxPlate)
            {
                var shrink = maxPlate / plateSize;
                (logoSize, innerSize, padding, plateSize) = (logoSize * shrink, innerSize * shrink, padding * shrink, maxPlate);
            }

            var center = new SKPoint(layout.Modules.MidX, layout.Modules.MidY);
            var bounds = SKRect.Create(center.X - (plateSize / 2f), center.Y - (plateSize / 2f), plateSize, plateSize);
            var logoBoxSize = shape == LogoBackgroundShape.Circle ? innerSize / MathF.Sqrt(2f) : logoSize;
            return new LogoPlate
            {
                Bounds = bounds,
                LogoBox = SKRect.Create(center.X - (logoBoxSize / 2f), center.Y - (logoBoxSize / 2f), logoBoxSize, logoBoxSize),
                Shape = shape,
                CornerRadius = Math.Min(plateSize * 0.2f, Math.Max(padding, 0f) * 3.4f)
            };
        }

        /// <summary>True when the module at (x, y) would be (partly) hidden by the plate.</summary>
        public bool Covers(SKPoint origin, float m, int x, int y)
        {
            var left = origin.X + (x * m);
            var top = origin.Y + (y * m);
            if (Shape == LogoBackgroundShape.Circle)
            {
                var dx = left + (m / 2f) - Bounds.MidX;
                var dy = top + (m / 2f) - Bounds.MidY;
                var reach = (Bounds.Width / 2f) + (m * 0.5f);
                return (dx * dx) + (dy * dy) < reach * reach;
            }
            return left + m > Bounds.Left && left < Bounds.Right && top + m > Bounds.Top && top < Bounds.Bottom;
        }
    }
}
