using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Controls;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Services;

namespace QuickQr.Features.Generate;

public enum DesignTab { Presets, Colors, Shapes, Logo, Frame, BrandKits }

/// <summary>A built-in starter design.</summary>
public sealed class StylePresetItem(string name, QrStyle style, IRelayCommand<StylePresetItem?> applyCommand)
{
    public string Name { get; } = name;
    public QrStyle Style { get; } = style;
    public string ForegroundColor => Style.Gradient?.StartColor ?? Style.ForegroundColor;
    public string AccentColor => Style.Gradient?.EndColor ?? Style.EyeColor ?? Style.ForegroundColor;
    public string BackgroundColor => Style.BackgroundColor;
    public IRelayCommand<StylePresetItem?> ApplyCommand { get; } = applyCommand;
    public string SemanticDescription => $"Apply the {Name} design";
}

/// <summary>A saved brand kit.</summary>
public sealed class BrandKitItem(BrandKit kit, IRelayCommand<BrandKitItem?> applyCommand, IAsyncRelayCommand<BrandKitItem?> deleteCommand)
{
    public BrandKit Kit { get; } = kit;
    public string Name => Kit.Name;
    public string ForegroundColor => Kit.Style.Gradient?.StartColor ?? Kit.Style.ForegroundColor;
    public string AccentColor => Kit.Style.Gradient?.EndColor ?? Kit.Style.EyeColor ?? Kit.Style.ForegroundColor;
    public string BackgroundColor => Kit.Style.BackgroundColor;
    public bool HasLogo => Kit.Style.Logo is not null;
    public IRelayCommand<BrandKitItem?> ApplyCommand { get; } = applyCommand;
    public IAsyncRelayCommand<BrandKitItem?> DeleteCommand { get; } = deleteCommand;
    public string ApplyDescription => $"Apply brand kit {Name}";
    public string DeleteDescription => $"Delete brand kit {Name}";
}

/// <summary>
/// Design studio state: colours (with a WCAG contrast check), gradient, shapes, logo, frame, quiet zone,
/// presets and brand kits. <see cref="BuildStyle"/> produces the <see cref="QrStyle"/>; <see cref="StyleChanged"/>
/// fires once per user change so the page can refresh the preview.
/// </summary>
public sealed partial class DesignStudioViewModel : ObservableObject
{
    public const double MinimumContrast = 3.0;
    private const long MaxLogoBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> StyleProperties =
    [
        nameof(ForegroundColor), nameof(BackgroundColor), nameof(TransparentBackground), nameof(UseCustomEyeColor),
        nameof(EyeColor), nameof(UseGradient), nameof(GradientStartColor), nameof(GradientEndColor), nameof(GradientAngle),
        nameof(LogoBytes), nameof(LogoSizePercent), nameof(FrameText), nameof(FrameColor), nameof(FrameTextColor),
        nameof(QuietZone), nameof(ShowHumanReadableText), nameof(SupportsAdvancedStyling)
    ];

    private readonly IPremiumGate _gate;
    private readonly IBrandKitStore _brandKitStore;
    private readonly IMediaPickerService _media;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _clock;
    private readonly ILogger<DesignStudioViewModel> _logger;
    private int _suspendNotifications;

    public DesignStudioViewModel(
        IPremiumGate gate,
        IBrandKitStore brandKitStore,
        IMediaPickerService media,
        IDialogService dialogs,
        TimeProvider clock,
        ILogger<DesignStudioViewModel> logger)
    {
        _gate = gate;
        _brandKitStore = brandKitStore;
        _media = media;
        _dialogs = dialogs;
        _clock = clock;
        _logger = logger;

        Tabs = new OptionGroup<DesignTab>()
            .Add(DesignTab.Presets, "Presets", "\ue65f")   // auto_awesome
            .Add(DesignTab.Colors, "Colours", "\ue40a")    // palette
            .Add(DesignTab.Shapes, "Shapes", "\ue72c")     // category
            .Add(DesignTab.Logo, "Logo", "\ue3f4")         // image
            .Add(DesignTab.Frame, "Frame", "\ue3c2")       // filter_frames
            .Add(DesignTab.BrandKits, "Brand kits", "\ue41d"); // style
        Tabs.SelectionChanged += (_, _) => NotifyTabs();

        GradientTypes = new OptionGroup<GradientType>()
            .Add(GradientType.Linear, "Linear")
            .Add(GradientType.Radial, "Radial");
        ModuleShapes = new OptionGroup<ModuleShape>()
            .Add(ModuleShape.Square, "Square")
            .Add(ModuleShape.Rounded, "Rounded")
            .Add(ModuleShape.Circle, "Dots")
            .Add(ModuleShape.Diamond, "Diamond")
            .Add(ModuleShape.VerticalBars, "Vertical bars")
            .Add(ModuleShape.HorizontalBars, "Horizontal bars");
        EyeFrameShapes = new OptionGroup<EyeFrameShape>()
            .Add(EyeFrameShape.Square, "Square")
            .Add(EyeFrameShape.Rounded, "Rounded")
            .Add(EyeFrameShape.Circle, "Circle");
        EyeBallShapes = new OptionGroup<EyeBallShape>()
            .Add(EyeBallShape.Square, "Square")
            .Add(EyeBallShape.Rounded, "Rounded")
            .Add(EyeBallShape.Circle, "Circle")
            .Add(EyeBallShape.Diamond, "Diamond");
        LogoBackgrounds = new OptionGroup<LogoBackgroundShape>()
            .Add(LogoBackgroundShape.Rounded, "Rounded")
            .Add(LogoBackgroundShape.Square, "Square")
            .Add(LogoBackgroundShape.Circle, "Circle")
            .Add(LogoBackgroundShape.None, "None");
        FrameStyles = new OptionGroup<FrameStyle>()
            .Add(FrameStyle.None, "None")
            .Add(FrameStyle.BannerBottom, "Banner below")
            .Add(FrameStyle.BannerTop, "Banner above")
            .Add(FrameStyle.RoundedBox, "Rounded box");

        foreach (var group in new OptionGroup[] { GradientTypes, ModuleShapes, EyeFrameShapes, EyeBallShapes, LogoBackgrounds, FrameStyles })
            group.SelectionChanged += OnOptionChanged;

        foreach (var (name, style) in StylePresets.All)
            Presets.Add(new StylePresetItem(name, style, ApplyPresetCommand));

        RefreshLocks();
        UpdateContrast();
    }

    /// <summary>Raised after any change that affects the rendered code.</summary>
    public event EventHandler? StyleChanged;

    public OptionGroup<DesignTab> Tabs { get; }
    public OptionGroup<GradientType> GradientTypes { get; }
    public OptionGroup<ModuleShape> ModuleShapes { get; }
    public OptionGroup<EyeFrameShape> EyeFrameShapes { get; }
    public OptionGroup<EyeBallShape> EyeBallShapes { get; }
    public OptionGroup<LogoBackgroundShape> LogoBackgrounds { get; }
    public OptionGroup<FrameStyle> FrameStyles { get; }
    public ObservableCollection<StylePresetItem> Presets { get; } = [];
    public ObservableCollection<BrandKitItem> BrandKits { get; } = [];

    public bool IsPresetsTab => Tabs.Value == DesignTab.Presets;
    public bool IsColorsTab => Tabs.Value == DesignTab.Colors;
    public bool IsShapesTab => Tabs.Value == DesignTab.Shapes;
    public bool IsLogoTab => Tabs.Value == DesignTab.Logo;
    public bool IsFrameTab => Tabs.Value == DesignTab.Frame;
    public bool IsBrandKitsTab => Tabs.Value == DesignTab.BrandKits;

    // ---------- Colours ----------
    [ObservableProperty]
    public partial string ForegroundColor { get; set; } = "#000000";

    [ObservableProperty]
    public partial string BackgroundColor { get; set; } = "#FFFFFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBackgroundColor))]
    public partial bool TransparentBackground { get; set; }

    [ObservableProperty]
    public partial bool UseCustomEyeColor { get; set; }

    [ObservableProperty]
    public partial string EyeColor { get; set; } = "#000000";

    public bool ShowBackgroundColor => !TransparentBackground;

    // ---------- Gradient ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSolidForeground), nameof(IsLinearGradient))]
    public partial bool UseGradient { get; set; }

    [ObservableProperty]
    public partial string GradientStartColor { get; set; } = "#6366F1";

    [ObservableProperty]
    public partial string GradientEndColor { get; set; } = "#8B5CF6";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GradientAngleText))]
    public partial double GradientAngle { get; set; } = 45;

    public bool ShowSolidForeground => !UseGradient;
    public bool IsLinearGradient => UseGradient && GradientTypes.Value == GradientType.Linear;
    public string GradientAngleText => $"{GradientAngle:0}°";

    // ---------- Logo ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo), nameof(HasNoLogo))]
    public partial byte[]? LogoBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogoSizeText), nameof(IsLogoLarge))]
    public partial double LogoSizePercent { get; set; } = 22;

    public bool HasLogo => LogoBytes is not null;
    public bool HasNoLogo => LogoBytes is null;
    public string LogoSizeText => string.Create(CultureInfo.CurrentCulture, $"{LogoSizePercent:0} %");
    public bool IsLogoLarge => LogoSizePercent > 25;

    // ---------- Frame ----------
    [ObservableProperty]
    public partial string FrameText { get; set; } = "SCAN ME";

    [ObservableProperty]
    public partial string FrameColor { get; set; } = "#000000";

    [ObservableProperty]
    public partial string FrameTextColor { get; set; } = "#FFFFFF";

    public bool HasFrame => FrameStyles.Value != FrameStyle.None;

    // ---------- Advanced ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuietZoneText), nameof(IsQuietZoneSmall))]
    public partial double QuietZone { get; set; } = 4;

    [ObservableProperty]
    public partial bool ShowHumanReadableText { get; set; } = true;

    public string QuietZoneText => $"{Math.Round(QuietZone):0} modules";
    public bool IsQuietZoneSmall => Math.Round(QuietZone) < 4;

    /// <summary>Shapes, eyes and logos only apply to QR codes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoAdvancedStyling))]
    public partial bool SupportsAdvancedStyling { get; set; } = true;

    public bool NoAdvancedStyling => !SupportsAdvancedStyling;

    [ObservableProperty]
    public partial bool IsOneDimensional { get; set; }

    // ---------- Contrast ----------
    [ObservableProperty]
    public partial string ContrastText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContrastWarning), nameof(IsContrastGood))]
    public partial string? ContrastWarning { get; private set; }

    public bool HasContrastWarning => ContrastWarning is not null;
    public bool IsContrastGood => ContrastWarning is null;

    // ---------- Locks (premium badges; selection stays possible for try-before-you-buy) ----------
    [ObservableProperty]
    public partial bool IsGradientLocked { get; private set; }

    [ObservableProperty]
    public partial bool AreShapesLocked { get; private set; }

    [ObservableProperty]
    public partial bool AreEyesLocked { get; private set; }

    [ObservableProperty]
    public partial bool IsLogoLocked { get; private set; }

    [ObservableProperty]
    public partial bool IsFrameLocked { get; private set; }

    [ObservableProperty]
    public partial bool AreBrandKitsLocked { get; private set; }

    [ObservableProperty]
    public partial bool HasBrandKits { get; private set; }

    public QrStyle BuildStyle() => new()
    {
        ForegroundColor = ForegroundColor,
        BackgroundColor = BackgroundColor,
        TransparentBackground = TransparentBackground,
        Gradient = UseGradient
            ? new GradientStyle(GradientTypes.Value, GradientStartColor, GradientEndColor, (float)Math.Round(GradientAngle))
            : null,
        ModuleShape = SupportsAdvancedStyling ? ModuleShapes.Value : ModuleShape.Square,
        EyeFrameShape = SupportsAdvancedStyling ? EyeFrameShapes.Value : EyeFrameShape.Square,
        EyeBallShape = SupportsAdvancedStyling ? EyeBallShapes.Value : EyeBallShape.Square,
        EyeColor = SupportsAdvancedStyling && UseCustomEyeColor ? EyeColor : null,
        Logo = SupportsAdvancedStyling && LogoBytes is { } logo
            ? new LogoOptions(logo, (float)Math.Round(LogoSizePercent / 100d, 2), Background: LogoBackgrounds.Value)
            : null,
        Frame = FrameStyles.Value == FrameStyle.None
            ? null
            : new FrameOptions(FrameStyles.Value, FrameText ?? string.Empty, FrameColor, FrameTextColor),
        QuietZoneModules = (int)Math.Round(QuietZone),
        ShowHumanReadableText = ShowHumanReadableText,
    };

    /// <summary>Loads every field from <paramref name="style"/> and raises a single <see cref="StyleChanged"/>.</summary>
    public void ApplyStyle(QrStyle style, bool keepLogo = false)
    {
        _suspendNotifications++;
        try
        {
            ForegroundColor = Normalize(style.ForegroundColor, "#000000");
            BackgroundColor = Normalize(style.BackgroundColor, "#FFFFFF");
            TransparentBackground = style.TransparentBackground;
            UseCustomEyeColor = style.EyeColor is not null;
            EyeColor = Normalize(style.EyeColor, ForegroundColor);
            UseGradient = style.Gradient is not null;
            if (style.Gradient is { } gradient)
            {
                GradientTypes.Value = gradient.Type;
                GradientStartColor = Normalize(gradient.StartColor, "#6366F1");
                GradientEndColor = Normalize(gradient.EndColor, "#8B5CF6");
                GradientAngle = gradient.AngleDegrees;
            }

            ModuleShapes.Value = style.ModuleShape;
            EyeFrameShapes.Value = style.EyeFrameShape;
            EyeBallShapes.Value = style.EyeBallShape;

            if (!keepLogo)
            {
                LogoBytes = style.Logo?.ImageBytes;
                if (style.Logo is { } logoOptions)
                {
                    LogoSizePercent = Math.Clamp(logoOptions.SizeRatio * 100d, 10, 30);
                    LogoBackgrounds.Value = logoOptions.Background;
                }
            }

            FrameStyles.Value = style.Frame?.Style ?? FrameStyle.None;
            if (style.Frame is { } frame)
            {
                FrameText = frame.Text;
                FrameColor = Normalize(frame.FrameColor, "#000000");
                FrameTextColor = Normalize(frame.TextColor, "#FFFFFF");
            }

            QuietZone = Math.Clamp(style.QuietZoneModules, 0, 10);
            ShowHumanReadableText = style.ShowHumanReadableText;
        }
        finally
        {
            _suspendNotifications--;
        }

        NotifyDerived();
        UpdateContrast();
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshLocks()
    {
        IsGradientLocked = !_gate.IsUnlocked(PremiumFeature.Gradients);
        AreShapesLocked = !_gate.IsUnlocked(PremiumFeature.ModuleShapes);
        AreEyesLocked = !_gate.IsUnlocked(PremiumFeature.EyeStyles);
        IsLogoLocked = !_gate.IsUnlocked(PremiumFeature.LogoEmbedding);
        IsFrameLocked = !_gate.IsUnlocked(PremiumFeature.Frames);
        AreBrandKitsLocked = !_gate.IsUnlocked(PremiumFeature.BrandKits);

        ModuleShapes.SetLocked(s => AreShapesLocked && s != ModuleShape.Square);
        EyeFrameShapes.SetLocked(s => AreEyesLocked && s != EyeFrameShape.Square);
        EyeBallShapes.SetLocked(s => AreEyesLocked && s != EyeBallShape.Square);
        FrameStyles.SetLocked(s => IsFrameLocked && s != FrameStyle.None);
        GradientTypes.SetLocked(_ => IsGradientLocked);
        Tabs.Find(DesignTab.BrandKits)!.IsLocked = AreBrandKitsLocked;
        Tabs.Find(DesignTab.Logo)!.IsLocked = IsLogoLocked;
        Tabs.Find(DesignTab.Frame)!.IsLocked = IsFrameLocked;
        Tabs.Find(DesignTab.Shapes)!.IsLocked = AreShapesLocked && AreEyesLocked;
    }

    public async Task LoadBrandKitsAsync()
    {
        try
        {
            var kits = await _brandKitStore.GetAllAsync();
            BrandKits.Clear();
            foreach (var kit in kits.OrderByDescending(k => k.CreatedAt))
                BrandKits.Add(new BrandKitItem(kit, ApplyBrandKitCommand, DeleteBrandKitCommand));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading brand kits failed");
        }

        HasBrandKits = BrandKits.Count > 0;
    }

    [RelayCommand]
    private void ApplyPreset(StylePresetItem? preset)
    {
        if (preset is null) return;
        ApplyStyle(preset.Style, keepLogo: true);
    }

    [RelayCommand]
    private void ApplyBrandKit(BrandKitItem? item)
    {
        if (item is null) return;
        ApplyStyle(item.Kit.Style, keepLogo: item.Kit.Style.Logo is null);
    }

    [RelayCommand]
    private async Task DeleteBrandKitAsync(BrandKitItem? item)
    {
        if (item is null) return;
        if (!await _dialogs.ConfirmAsync("Delete brand kit", $"Delete \"{item.Name}\"? This can't be undone.", "Delete", "Cancel"))
            return;

        try
        {
            await _brandKitStore.DeleteAsync(item.Kit.Id);
            await LoadBrandKitsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deleting brand kit {Id} failed", item.Kit.Id);
            await _dialogs.AlertAsync("Couldn't delete", "The brand kit could not be deleted. Please try again.");
        }
    }

    [RelayCommand]
    private async Task SaveBrandKitAsync()
    {
        if (!await _gate.EnsureAsync(PremiumFeature.BrandKits)) return;

        var name = await _dialogs.PromptAsync("Save brand kit", "Name this design so you can reuse it in one tap.",
            "Save", "Cancel", placeholder: "My brand", maxLength: 40);
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var result = await _brandKitStore.SaveAsync(new BrandKit(Guid.NewGuid(), name.Trim(), BuildStyle(), _clock.GetUtcNow()));
            if (result.IsFailure)
            {
                await _dialogs.AlertAsync("Couldn't save brand kit", result.Error.Message);
                return;
            }

            await _dialogs.ToastAsync($"Saved \"{name.Trim()}\"");
            await LoadBrandKitsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving brand kit failed");
            await _dialogs.AlertAsync("Couldn't save brand kit", "Please try again.");
        }
    }

    [RelayCommand]
    private async Task PickLogoAsync()
    {
        try
        {
            var file = await _media.PickImageAsync();
            if (file is null) return;

            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length == 0) return;
            if (buffer.Length > MaxLogoBytes)
            {
                await _dialogs.AlertAsync("Logo too large", "Choose an image smaller than 5 MB.");
                return;
            }

            LogoBytes = buffer.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Picking a logo failed");
            await _dialogs.AlertAsync("Couldn't add logo", "The image could not be opened. Try a PNG or JPEG file.");
        }
    }

    [RelayCommand]
    private void RemoveLogo() => LogoBytes = null;

    [RelayCommand]
    private void ResetDesign() => ApplyStyle(QrStyle.Default);

    [RelayCommand]
    private void SwapColors()
    {
        _suspendNotifications++;
        try
        {
            (ForegroundColor, BackgroundColor) = (BackgroundColor, ForegroundColor);
        }
        finally
        {
            _suspendNotifications--;
        }

        UpdateContrast();
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suspendNotifications > 0 || e.PropertyName is null || !StyleProperties.Contains(e.PropertyName)) return;

        UpdateContrast();
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOptionChanged(object? sender, EventArgs e)
    {
        if (_suspendNotifications > 0) return;
        NotifyDerived();
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(IsLinearGradient));
        OnPropertyChanged(nameof(HasFrame));
    }

    private void NotifyTabs()
    {
        OnPropertyChanged(nameof(IsPresetsTab));
        OnPropertyChanged(nameof(IsColorsTab));
        OnPropertyChanged(nameof(IsShapesTab));
        OnPropertyChanged(nameof(IsLogoTab));
        OnPropertyChanged(nameof(IsFrameTab));
        OnPropertyChanged(nameof(IsBrandKitsTab));
    }

    /// <summary>WCAG contrast between the darkest/lightest code colour and the background (light surface assumed when transparent).</summary>
    private void UpdateContrast()
    {
        var background = TransparentBackground ? "#FFFFFF" : BackgroundColor;
        var codeColors = UseGradient ? new[] { GradientStartColor, GradientEndColor } : new[] { ForegroundColor };

        double? worst = null;
        var lightOnDark = false;
        var backgroundLuminance = HexColor.RelativeLuminance(background);
        foreach (var color in codeColors)
        {
            if (HexColor.ContrastRatio(color, background) is { } ratio)
                worst = worst is null ? ratio : Math.Min(worst.Value, ratio);
            if (HexColor.RelativeLuminance(color) is { } l && backgroundLuminance is { } bl && l > bl)
                lightOnDark = true;
        }

        if (worst is not { } value)
        {
            ContrastText = "Enter valid colours to check contrast";
            ContrastWarning = "One of the colours is not a valid hex value (#RRGGBB).";
            return;
        }

        ContrastText = string.Create(CultureInfo.CurrentCulture, $"Contrast {value:0.0}:1{(TransparentBackground ? " on a light surface" : string.Empty)}");

        var warnings = new List<string>(2);
        if (value < MinimumContrast)
            warnings.Add(string.Create(CultureInfo.CurrentCulture, $"Low contrast ({value:0.0}:1). Aim for at least 3:1 so every scanner can read the code."));
        if (lightOnDark)
            warnings.Add("Light code on a dark background: many scanners can't read inverted codes. Use a dark code on a light background.");
        ContrastWarning = warnings.Count > 0 ? string.Join(" ", warnings) : null;
    }

    private static string Normalize(string? hex, string fallback) =>
        HexColor.TryNormalize(hex, out var normalized) ? normalized : fallback;
}
