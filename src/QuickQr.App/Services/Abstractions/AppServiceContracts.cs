using QuickQr.Core;
using QuickQr.Core.Content;
using QuickQr.Core.Licensing;
using QuickQr.Core.Settings;

namespace QuickQr.Services;

// App-layer ports. ViewModels depend on these interfaces only, never on MAUI static APIs,
// so they stay unit-testable and platform behaviour lives in one place.

public interface IDialogService
{
    Task AlertAsync(string title, string message, string cancel = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
    /// <returns>The chosen button text, or null when cancelled.</returns>
    Task<string?> ActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons);
    Task<string?> PromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel",
        string? placeholder = null, string? initialValue = null, int maxLength = -1, Keyboard? keyboard = null);
    /// <summary>Short non-blocking confirmation (CommunityToolkit Toast).</summary>
    Task ToastAsync(string message, bool longDuration = false);
    /// <summary>Snackbar with an optional action (e.g. "Undo").</summary>
    Task SnackbarAsync(string message, string? actionText = null, Func<Task>? action = null);
}

public interface INavigationService
{
    /// <summary>Shell navigation, e.g. <c>GoToAsync(Routes.HistoryDetail, new() { ["id"] = id })</c>.</summary>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);
    Task GoBackAsync();
    /// <summary>Switches to a root tab (<see cref="Routes.Create"/>, <see cref="Routes.Scan"/>, ...).</summary>
    Task GoToTabAsync(string tabRoute, IDictionary<string, object>? parameters = null);
}

public interface IShareService
{
    Task ShareTextAsync(string title, string text);
    Task ShareFileAsync(string title, string filePath, string contentType);
    Task ShareFilesAsync(string title, IReadOnlyList<string> filePaths);
}

public interface IFileExportService
{
    /// <summary>Saves an image to the device photo library / Pictures folder. Returns the saved location.</summary>
    Task<Result<string>> SaveImageToGalleryAsync(byte[] imageBytes, string fileName, string mimeType, CancellationToken cancellationToken = default);
    /// <summary>Lets the user choose a location (system save dialog) and writes the stream there.</summary>
    Task<Result<string>> SaveFileAsync(Stream data, string fileName, CancellationToken cancellationToken = default);
    /// <summary>Writes to the app cache (for sharing) and returns the full path.</summary>
    Task<string> WriteTempFileAsync(byte[] data, string fileName, CancellationToken cancellationToken = default);
}

public interface IMediaPickerService
{
    /// <summary>Picks a photo from the gallery/file system. Null when cancelled.</summary>
    Task<FileResult?> PickImageAsync();
    /// <summary>Picks a document (CSV, TXT, ...). Null when cancelled.</summary>
    Task<FileResult?> PickFileAsync(IReadOnlyList<string> extensions);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}

public interface IHapticsService
{
    void Click();
    void Success();
    void Error();
}

/// <summary>Vibration/sound after a successful scan, honouring <see cref="AppSettings"/>.</summary>
public interface IScanFeedbackService
{
    void OnScanSucceeded();
}

/// <summary>Executes a <see cref="ContentAction"/> (open link, connect Wi-Fi, add contact, calendar, payment app, ...).</summary>
public interface IContentActionExecutor
{
    Task ExecuteAsync(ContentAction action, QrContent content, CancellationToken cancellationToken = default);
}

/// <summary>Paywall gate used by every premium entry point.</summary>
public interface IPremiumGate
{
    bool IsUnlocked(PremiumFeature feature);
    /// <summary>True when unlocked; otherwise opens the paywall (highlighting <paramref name="feature"/>) and returns true only if the user upgraded.</summary>
    Task<bool> EnsureAsync(PremiumFeature feature);
    Task ShowPaywallAsync(PremiumFeature? highlight = null);
}

public interface IThemeService
{
    void Apply(ThemePreference preference);
}

public interface IAppLockService
{
    Task<bool> IsAvailableAsync();
    Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>Feature hooks into app lifecycle (app lock, update checks, ...). All registered hooks run in order.</summary>
public interface IAppLifecycleHook
{
    Task OnStartAsync();
    Task OnResumeAsync();
    void OnSleep();
}
