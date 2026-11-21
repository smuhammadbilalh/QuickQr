using Microsoft.Extensions.Logging;
using QuickQr.Core.Settings;

namespace QuickQr.Services;

public sealed class ShareService : IShareService
{
    public Task ShareTextAsync(string title, string text) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Text = text,
        }));

    public Task ShareFileAsync(string title, string filePath, string contentType) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath, contentType),
        }));

    public Task ShareFilesAsync(string title, IReadOnlyList<string> filePaths) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = title,
            Files = filePaths.Select(p => new ShareFile(p)).ToList(),
        }));
}

public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) =>
        MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));

    public Task<string?> GetTextAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
            Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null);
}

/// <summary>Haptics are optional hardware: unsupported devices are silently ignored.</summary>
public sealed class HapticsService(ILogger<HapticsService> logger) : IHapticsService
{
    public void Click() => Perform(HapticFeedbackType.Click);

    public void Success() => Perform(HapticFeedbackType.LongPress);

    public void Error()
    {
        try
        {
            if (Vibration.Default.IsSupported)
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(120));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Vibration unavailable");
        }
    }

    private void Perform(HapticFeedbackType type)
    {
        try
        {
            if (HapticFeedback.Default.IsSupported)
                HapticFeedback.Default.Perform(type);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Haptic feedback unavailable");
        }
    }
}

public sealed class ThemeService : IThemeService
{
    public void Apply(ThemePreference preference)
    {
        if (Application.Current is not { } app) return;

        app.UserAppTheme = preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }
}
