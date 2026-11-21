using QuickQr.Core.Barcodes;

namespace QuickQr.Core.Settings;

public enum ThemePreference { System = 0, Light = 1, Dark = 2 }

/// <summary>User preferences. Immutable; change with <c>with</c> expressions and save via <see cref="IAppSettingsService"/>.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    // Scanning
    public bool VibrateOnScan { get; init; } = true;
    public bool BeepOnScan { get; init; }
    public bool AutoCopyScans { get; init; }
    /// <summary>Open URLs immediately when the safety analyzer rates them Safe.</summary>
    public bool AutoOpenSafeLinks { get; init; }
    public bool SaveScansToHistory { get; init; } = true;
    /// <summary>Run online reputation checks on scanned links (premium).</summary>
    public bool OnlineLinkProtection { get; init; } = true;

    // Generation
    public bool SaveGeneratedToHistory { get; init; } = true;
    public ErrorCorrectionLevel DefaultErrorCorrection { get; init; } = ErrorCorrectionLevel.Medium;
    public BarcodeFormat DefaultFormat { get; init; } = BarcodeFormat.QrCode;
    public int DefaultExportSize { get; init; } = 1024;

    // Security
    public bool AppLockEnabled { get; init; }

    // Onboarding
    public bool HasCompletedOnboarding { get; init; }
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}
