using Microsoft.Extensions.Logging;
using QuickQr.Core.Updates;

namespace QuickQr.Services.Lifecycle;

/// <summary>
/// Sideloaded builds only (<see cref="AppConfiguration.UpdateChecksEnabled"/>): a few seconds after start, checks the
/// GitHub releases feed (throttled by the service) and offers the download. Never blocks start-up.
/// </summary>
public sealed class UpdateCheckLifecycleHook(
    IUpdateService updates,
    IDialogService dialogs,
    IExternalLauncher launcher,
    AppLockLifecycleHook appLock,
    ILogger<UpdateCheckLifecycleHook> logger) : IAppLifecycleHook
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
    private const int MaxNotesLength = 700;
    private int _started;

    public Task OnStartAsync()
    {
        if (!AppConfiguration.UpdateChecksEnabled || Interlocked.Exchange(ref _started, 1) == 1)
            return Task.CompletedTask;

        _ = Task.Run(CheckLaterAsync);
        return Task.CompletedTask;
    }

    public Task OnResumeAsync() => Task.CompletedTask;

    public void OnSleep()
    {
    }

    private async Task CheckLaterAsync()
    {
        try
        {
            await Task.Delay(StartDelay);
            var result = await updates.CheckAsync(force: false);
            if (result.Status != UpdateStatus.UpdateAvailable || result.DownloadUrl is null) return;

            // Don't stack a dialog on top of the lock screen.
            for (var i = 0; appLock.IsLocked && i < 120; i++)
                await Task.Delay(TimeSpan.FromSeconds(1));
            if (appLock.IsLocked) return;

            await ShowUpdateAsync(dialogs, launcher, result);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Background update check failed");
        }
    }

    /// <summary>The shared "update available" dialog (also used by Settings → Check for updates).</summary>
    public static async Task ShowUpdateAsync(IDialogService dialogs, IExternalLauncher launcher, UpdateCheckResult result)
    {
        var notes = (result.ReleaseNotes ?? string.Empty).Trim();
        if (notes.Length > MaxNotesLength) notes = notes[..MaxNotesLength].TrimEnd() + "…";

        var message = $"You have {result.CurrentVersion}." + (notes.Length > 0 ? "\n\nWhat's new:\n" + notes : string.Empty);
        var title = $"QuickQr {result.LatestVersion} is available";
        if (await dialogs.ConfirmAsync(title, message, "Download", "Later") && result.DownloadUrl is not null)
            await launcher.OpenUrlAsync(result.DownloadUrl.AbsoluteUri);
    }
}
