using Microsoft.Extensions.Logging;

namespace QuickQr.Services.Lifecycle;

/// <summary>Opens the Scan tab when another app shared text or an image with QuickQr (Android share sheet).</summary>
public sealed class SharedContentLifecycleHook(
    ISharedContentInbox inbox,
    INavigationService navigation,
    ILogger<SharedContentLifecycleHook> logger) : IAppLifecycleHook
{
    private static readonly TimeSpan ShellWait = TimeSpan.FromSeconds(5);

    public Task OnStartAsync() => OpenScanIfPendingAsync();

    public Task OnResumeAsync() => OpenScanIfPendingAsync();

    public void OnSleep()
    {
    }

    private async Task OpenScanIfPendingAsync()
    {
        if (!inbox.HasContent) return;
        try
        {
            // On a cold start the shell may still be attaching its handler.
            var deadline = DateTime.UtcNow + ShellWait;
            while (Shell.Current?.Handler is null && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            await navigation.GoToTabAsync(Routes.Scan);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not open the scan tab for shared content");
        }
    }
}
