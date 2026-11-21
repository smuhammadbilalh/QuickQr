using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Barcodes;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;
using QuickQr.Core.Settings;
using QuickQr.Core.Updates;
using QuickQr.Features.About;
using QuickQr.Mvvm;
using QuickQr.Services;
using QuickQr.Services.Lifecycle;

namespace QuickQr.Features.Settings;

/// <summary>
/// Settings tab. Every change is persisted immediately with <c>SaveAsync(current with { ... })</c>; premium options
/// (high-resolution export, advanced formats, app lock) go through the paywall gate and revert when not unlocked.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    public const string PrivacyPolicyUrl = "https://github.com/smuhammadbilalh/QuickQr/blob/master/PRIVACY.md";
    public const string RepositoryUrl = "https://github.com/smuhammadbilalh/QuickQr";
    private const string AndroidPackageName = "muhammadbilal.quickqr";

    private static readonly int[] ExportSizes = [512, 1024, 2048, 4096];
    private static readonly ErrorCorrectionLevel[] ErrorCorrectionLevels =
        [ErrorCorrectionLevel.Low, ErrorCorrectionLevel.Medium, ErrorCorrectionLevel.Quartile, ErrorCorrectionLevel.High];

    private readonly IAppSettingsService _settings;
    private readonly IEntitlementService _entitlements;
    private readonly IPremiumGate _premium;
    private readonly IAppLockService _appLock;
    private readonly IHistoryService _history;
    private readonly IUpdateService _updates;
    private readonly IAppInfoProvider _appInfo;
    private readonly IExternalLauncher _launcher;
    private readonly INavigationService _navigation;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _loading;
    private bool _subscribed;

    public SettingsViewModel(
        IDialogService dialogs,
        ILogger<SettingsViewModel> logger,
        IAppSettingsService settings,
        IEntitlementService entitlements,
        IPremiumGate premium,
        IAppLockService appLock,
        IHistoryService history,
        IUpdateService updates,
        IAppInfoProvider appInfo,
        IExternalLauncher launcher,
        INavigationService navigation) : base(dialogs, logger)
    {
        _settings = settings;
        _entitlements = entitlements;
        _premium = premium;
        _appLock = appLock;
        _history = history;
        _updates = updates;
        _appInfo = appInfo;
        _launcher = launcher;
        _navigation = navigation;

        Title = "Settings";
        FormatOptions = BarcodeFormatExtensions.Writable.Select(f => f.DisplayName()).ToList();
        VersionText = $"Version {appInfo.Version} (build {appInfo.Build})";
        Load(settings.Current);
    }

    // ---------- Options ----------

    public List<string> ThemeOptions { get; } = ["System default", "Light", "Dark"];
    public List<string> ErrorCorrectionOptions { get; } = ["Low (7 %)", "Medium (15 %)", "Quartile (25 %)", "High (30 %)"];
    public List<string> FormatOptions { get; }
    public List<string> ExportSizeOptions { get; } = ExportSizes.Select(s => $"{s} × {s} px").ToList();

    // ---------- Plan ----------

    public string TierName => _entitlements.Tier switch
    {
        SubscriptionTier.Business => "QuickQr Business",
        SubscriptionTier.Pro => "QuickQr Pro",
        _ => "QuickQr Free",
    };

    public string PlanDescription => _entitlements.Tier switch
    {
        SubscriptionTier.Business => "Every feature, including bulk generation and print sheets. Thank you!",
        SubscriptionTier.Pro => "Design studio, vector export, continuous scanning, app lock and more. Thank you!",
        _ => "Everything you need to create and scan, with no ads and no account.",
    };

    public bool CanUpgrade => _entitlements.Tier < SubscriptionTier.Business;
    public string UpgradeText => _entitlements.Tier == SubscriptionTier.Free ? "Upgrade to Pro" : "Upgrade to Business";
    public bool IsPaid => _entitlements.Tier > SubscriptionTier.Free;

    // ---------- Settings ----------

    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool VibrateOnScan { get; set; }

    [ObservableProperty]
    public partial bool BeepOnScan { get; set; }

    [ObservableProperty]
    public partial bool AutoCopyScans { get; set; }

    [ObservableProperty]
    public partial bool AutoOpenSafeLinks { get; set; }

    [ObservableProperty]
    public partial bool SaveScansToHistory { get; set; }

    [ObservableProperty]
    public partial bool SaveGeneratedToHistory { get; set; }

    [ObservableProperty]
    public partial int SelectedErrorCorrectionIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedFormatIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedExportSizeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppLockDescription))]
    public partial bool AppLockEnabled { get; set; }

    public bool IsAppLockLocked => !_premium.IsUnlocked(PremiumFeature.AppLock);
    public bool IsAdvancedFormatsLocked => !_premium.IsUnlocked(PremiumFeature.AdvancedFormats);
    public bool IsHighResolutionLocked => !_premium.IsUnlocked(PremiumFeature.HighResolutionExport);

    public string AppLockDescription => AppLockEnabled
        ? "Fingerprint, face or device PIN is needed to open QuickQr."
        : "Require fingerprint, face or device PIN to open QuickQr.";

    // ---------- About ----------

    public string VersionText { get; }
    public bool ShowUpdateCheck => AppConfiguration.UpdateChecksEnabled;

    // ---------- Lifecycle ----------

    public override Task OnAppearingAsync()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            _settings.SettingsChanged += OnSettingsChanged;
            _entitlements.EntitlementsChanged += OnEntitlementsChanged;
        }

        Load(_settings.Current);
        RefreshPlan();
        return Task.CompletedTask;
    }

    public override Task OnDisappearingAsync()
    {
        if (_subscribed)
        {
            _subscribed = false;
            _settings.SettingsChanged -= OnSettingsChanged;
            _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        }

        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, AppSettings e) => MainThread.BeginInvokeOnMainThread(() => Load(e));

    private void OnEntitlementsChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(RefreshPlan);

    private void Load(AppSettings s)
    {
        _loading = true;
        try
        {
            SelectedThemeIndex = (int)s.Theme is >= 0 and <= 2 ? (int)s.Theme : 0;
            VibrateOnScan = s.VibrateOnScan;
            BeepOnScan = s.BeepOnScan;
            AutoCopyScans = s.AutoCopyScans;
            AutoOpenSafeLinks = s.AutoOpenSafeLinks;
            SaveScansToHistory = s.SaveScansToHistory;
            SaveGeneratedToHistory = s.SaveGeneratedToHistory;
            SelectedErrorCorrectionIndex = Math.Max(0, Array.IndexOf(ErrorCorrectionLevels, s.DefaultErrorCorrection));
            SelectedFormatIndex = Math.Max(0, IndexOfFormat(s.DefaultFormat));
            SelectedExportSizeIndex = NearestExportSizeIndex(s.DefaultExportSize);
            AppLockEnabled = s.AppLockEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshPlan()
    {
        OnPropertyChanged(nameof(TierName));
        OnPropertyChanged(nameof(PlanDescription));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(UpgradeText));
        OnPropertyChanged(nameof(IsPaid));
        OnPropertyChanged(nameof(IsAppLockLocked));
        OnPropertyChanged(nameof(IsAdvancedFormatsLocked));
        OnPropertyChanged(nameof(IsHighResolutionLocked));
    }

    // ---------- Persistence ----------

    partial void OnSelectedThemeIndexChanged(int value)
    {
        // A picker reports -1 while its items are (re)loading: never persist that.
        if (value is >= 0 and <= 2)
            Save(s => s with { Theme = (ThemePreference)value });
    }

    partial void OnVibrateOnScanChanged(bool value) => Save(s => s with { VibrateOnScan = value });
    partial void OnBeepOnScanChanged(bool value) => Save(s => s with { BeepOnScan = value });
    partial void OnAutoCopyScansChanged(bool value) => Save(s => s with { AutoCopyScans = value });
    partial void OnAutoOpenSafeLinksChanged(bool value) => Save(s => s with { AutoOpenSafeLinks = value });
    partial void OnSaveScansToHistoryChanged(bool value) => Save(s => s with { SaveScansToHistory = value });
    partial void OnSaveGeneratedToHistoryChanged(bool value) => Save(s => s with { SaveGeneratedToHistory = value });

    partial void OnSelectedErrorCorrectionIndexChanged(int value)
    {
        if (value >= 0 && value < ErrorCorrectionLevels.Length)
            Save(s => s with { DefaultErrorCorrection = ErrorCorrectionLevels[value] });
    }

    partial void OnSelectedFormatIndexChanged(int oldValue, int newValue)
    {
        if (_loading || newValue < 0 || newValue >= BarcodeFormatExtensions.Writable.Count) return;
        var format = BarcodeFormatExtensions.Writable[newValue];
        _ = ApplyGatedAsync(
            format != BarcodeFormat.QrCode ? PremiumFeature.AdvancedFormats : null,
            s => s with { DefaultFormat = format },
            () => SelectedFormatIndex = Math.Max(0, oldValue));
    }

    partial void OnSelectedExportSizeIndexChanged(int oldValue, int newValue)
    {
        if (_loading || newValue < 0 || newValue >= ExportSizes.Length) return;
        var size = ExportSizes[newValue];
        _ = ApplyGatedAsync(
            size > FeatureCatalog.FreeMaxExportSize ? PremiumFeature.HighResolutionExport : null,
            s => s with { DefaultExportSize = size },
            () => SelectedExportSizeIndex = Math.Max(0, oldValue));
    }

    partial void OnAppLockEnabledChanged(bool value)
    {
        if (_loading) return;
        if (value) _ = EnableAppLockAsync();
        else Save(s => s with { AppLockEnabled = false });
    }

    private async Task EnableAppLockAsync()
    {
        try
        {
            if (!await _premium.EnsureAsync(PremiumFeature.AppLock))
            {
                Revert(() => AppLockEnabled = false);
                return;
            }

            RefreshPlan();
            if (!await _appLock.IsAvailableAsync())
            {
                Revert(() => AppLockEnabled = false);
                await Dialogs.AlertAsync("Set up a screen lock first",
                    "App lock uses your device's fingerprint, face or PIN. Set a screen lock in your device settings, then try again.");
                return;
            }

            // Authenticate once so nobody locks themselves out with a lock they can't open.
            if (!await _appLock.AuthenticateAsync("Confirm it's you to turn on App lock"))
            {
                Revert(() => AppLockEnabled = false);
                return;
            }

            await SaveCoreAsync(s => s with { AppLockEnabled = true });
            await Dialogs.ToastAsync("App lock is on");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Enabling app lock failed");
            Revert(() => AppLockEnabled = false);
            await Dialogs.AlertAsync("App lock", "App lock couldn't be turned on. Please try again.");
        }
    }

    private async Task ApplyGatedAsync(PremiumFeature? feature, Func<AppSettings, AppSettings> change, Action revert)
    {
        try
        {
            if (feature is { } f && !await _premium.EnsureAsync(f))
            {
                Revert(revert);
                return;
            }

            RefreshPlan();
            await SaveCoreAsync(change);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Saving a setting failed");
            Revert(revert);
        }
    }

    private void Revert(Action revert)
    {
        _loading = true;
        try { revert(); }
        finally { _loading = false; }
    }

    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (_loading) return;
        _ = SaveSafeAsync(change);
    }

    private async Task SaveSafeAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            await SaveCoreAsync(change);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Saving settings failed");
            await Dialogs.AlertAsync("Settings not saved", "Your change couldn't be saved. Please try again.");
            Load(_settings.Current);
        }
    }

    private async Task SaveCoreAsync(Func<AppSettings, AppSettings> change)
    {
        await _saveGate.WaitAsync();
        try
        {
            await _settings.SaveAsync(change(_settings.Current));
        }
        finally
        {
            _saveGate.Release();
        }
    }

    // ---------- Commands ----------

    [RelayCommand]
    private Task UpgradeAsync() => _premium.ShowPaywallAsync();

    [RelayCommand]
    private Task RestorePurchasesAsync() => RunBusyAsync(async () =>
    {
        var outcome = await _entitlements.RestoreAsync();
        RefreshPlan();
        switch (outcome.Status)
        {
            case PurchaseStatus.Restored:
            case PurchaseStatus.AlreadyOwned:
                await Dialogs.AlertAsync("Purchases restored", $"Welcome back to {TierName}.");
                break;
            case PurchaseStatus.Cancelled:
                break;
            case PurchaseStatus.NotSupported:
                await Dialogs.AlertAsync("Not available", outcome.Message ?? "In-app purchases are not available on this device.");
                break;
            default:
                await Dialogs.AlertAsync("Nothing to restore", outcome.Message ?? "No purchases were found for this store account.");
                break;
        }
    }, "Restore failed");

    [RelayCommand]
    private Task ClearHistoryAsync() => RunBusyAsync(async () =>
    {
        var count = await _history.CountAsync();
        if (count == 0)
        {
            await Dialogs.ToastAsync("History is already empty");
            return;
        }

        if (!await Dialogs.ConfirmAsync("Clear all history?",
                $"This permanently deletes all {count} scanned and created codes, including favourites. This can't be undone.",
                "Delete all", "Cancel"))
            return;

        await _history.ClearAsync();
        await Dialogs.ToastAsync("History cleared");
    }, "Couldn't clear history");

    [RelayCommand]
    private Task CheckForUpdatesAsync() => RunBusyAsync(async () =>
    {
        var result = await _updates.CheckAsync(force: true);
        switch (result.Status)
        {
            case UpdateStatus.UpdateAvailable:
                await UpdateCheckLifecycleHook.ShowUpdateAsync(Dialogs, _launcher, result);
                break;
            case UpdateStatus.UpToDate:
                await Dialogs.AlertAsync("You're up to date", $"QuickQr {result.CurrentVersion} is the latest version.");
                break;
            case UpdateStatus.Ahead:
                await Dialogs.AlertAsync("Preview build", $"You're running {result.CurrentVersion}, newer than the latest release ({result.LatestVersion}).");
                break;
            case UpdateStatus.Offline:
                await Dialogs.AlertAsync("You're offline", "Connect to the internet to check for updates.");
                break;
            case UpdateStatus.Throttled:
                await Dialogs.AlertAsync("Try again later", "Update checks are temporarily limited. Please try again in a few minutes.");
                break;
            default:
                await Dialogs.AlertAsync("Couldn't check for updates", result.Error ?? "Please try again later.");
                break;
        }
    }, "Update check failed");

    [RelayCommand]
    private Task OpenAboutAsync() => _navigation.GoToAsync(Routes.About);

    [RelayCommand]
    private Task OpenLicensesAsync() => _navigation.GoToAsync(AboutRoutes.Licenses);

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        if (!await _launcher.OpenUrlAsync(PrivacyPolicyUrl))
            await Dialogs.AlertAsync("Couldn't open the link", PrivacyPolicyUrl);
    }

    [RelayCommand]
    private async Task RateAppAsync()
    {
        if (_appInfo.Platform == DevicePlatform.Android.ToString())
        {
            if (await _launcher.TryOpenAsync($"market://details?id={AndroidPackageName}")) return;
            await _launcher.OpenUrlAsync($"https://play.google.com/store/apps/details?id={AndroidPackageName}");
            return;
        }

        // No store listing elsewhere yet: a GitHub star is the "rating".
        await _launcher.OpenUrlAsync(RepositoryUrl);
    }

    [RelayCommand]
    private Task ResetSettingsAsync() => RunBusyAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Reset settings?",
                "All settings return to their defaults. Your history and purchases are not affected.", "Reset", "Cancel"))
            return;

        await _settings.ResetAsync();
        Load(_settings.Current);
        await Dialogs.ToastAsync("Settings reset");
    }, "Couldn't reset settings");

    private static int IndexOfFormat(BarcodeFormat format)
    {
        var list = BarcodeFormatExtensions.Writable;
        for (var i = 0; i < list.Count; i++)
            if (list[i] == format) return i;
        return 0;
    }

    private static int NearestExportSizeIndex(int size)
    {
        var best = 1;
        for (var i = 0; i < ExportSizes.Length; i++)
            if (Math.Abs(ExportSizes[i] - size) < Math.Abs(ExportSizes[best] - size)) best = i;
        return best;
    }
}
