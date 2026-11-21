namespace QuickQr.Core.Rendering;

/// <summary>A named, reusable style preset (premium "brand kit"). The style may include logo bytes.</summary>
public sealed record BrandKit(Guid Id, string Name, QrStyle Style, DateTimeOffset CreatedAt);

/// <summary>Persists brand kits on the device. Requires <see cref="Licensing.PremiumFeature.BrandKits"/> to save.</summary>
public interface IBrandKitStore
{
    Task<IReadOnlyList<BrandKit>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(BrandKit kit, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Built-in, free starter designs shown in the design studio.</summary>
public static class StylePresets
{
    public static IReadOnlyList<(string Name, QrStyle Style)> All { get; } =
    [
        ("Classic", QrStyle.Default),
        ("Midnight", new QrStyle { ForegroundColor = "#0F172A", BackgroundColor = "#FFFFFF", ModuleShape = ModuleShape.Rounded, EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Rounded }),
        ("Indigo", new QrStyle { ForegroundColor = "#4F46E5", BackgroundColor = "#FFFFFF", Gradient = new GradientStyle(GradientType.Linear, "#6366F1", "#8B5CF6"), ModuleShape = ModuleShape.Circle, EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Circle }),
        ("Ocean", new QrStyle { ForegroundColor = "#0891B2", BackgroundColor = "#ECFEFF", Gradient = new GradientStyle(GradientType.Radial, "#06B6D4", "#0E7490"), ModuleShape = ModuleShape.Rounded, EyeFrameShape = EyeFrameShape.Circle, EyeBallShape = EyeBallShape.Circle }),
        ("Sunset", new QrStyle { ForegroundColor = "#EA580C", BackgroundColor = "#FFF7ED", Gradient = new GradientStyle(GradientType.Linear, "#F97316", "#DB2777", 135f), ModuleShape = ModuleShape.Diamond, EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Diamond }),
        ("Forest", new QrStyle { ForegroundColor = "#166534", BackgroundColor = "#F0FDF4", ModuleShape = ModuleShape.VerticalBars, EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Rounded, EyeColor = "#15803D" }),
        ("Scan me", new QrStyle { ForegroundColor = "#111827", ModuleShape = ModuleShape.Rounded, EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Rounded, Frame = new FrameOptions(FrameStyle.BannerBottom, "SCAN ME", "#111827", "#FFFFFF") }),
    ];
}
