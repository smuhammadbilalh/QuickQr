using Microsoft.Extensions.Logging;
using QuickQr.Core.Licensing;
using QuickQr.Core.Settings;
using QuickQr.Features.Settings;

namespace QuickQr.Services.Lifecycle;

/// <summary>
/// Shows <see cref="LockPage"/> when the app starts, and when it returns after more than
/// <see cref="BackgroundGrace"/> in the background, if App lock is on and unlocked by the current plan.
/// </summary>
public sealed class AppLockLifecycleHook(
    IAppSettingsService settings,
    IEntitlementService entitlements,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<AppLockLifecycleHook> logger) : IAppLifecycleHook
{
    internal static readonly TimeSpan BackgroundGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShellWait = TimeSpan.FromSeconds(5);

    private DateTimeOffset? _backgroundedAt;
    private volatile bool _isLocked;

    /// <summary>True while the lock screen is on top (other hooks wait before showing dialogs).</summary>
    public bool IsLocked => _isLocked;

    private bool ShouldLock => settings.Current.AppLockEnabled && entitlements.HasFeature(PremiumFeature.AppLock);

    public Task OnStartAsync() => ShouldLock ? ShowLockAsync() : Task.CompletedTask;

    public Task OnResumeAsync()
    {
        var since = _backgroundedAt;
        _backgroundedAt = null;
        if (since is null || !ShouldLock) return Task.CompletedTask;

        return clock.GetUtcNow() - since.Value > BackgroundGrace ? ShowLockAsync() : Task.CompletedTask;
    }

    public void OnSleep() => _backgroundedAt ??= clock.GetUtcNow();

    private Task ShowLockAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_isLocked) return;

            var root = await WaitForRootPageAsync();
            if (root is null)
            {
                logger.LogWarning("App lock could not be shown: no window page");
                return;
            }

            if (root.Navigation.ModalStack.Any(p => p is LockPage)) return;

            var page = services.GetRequiredService<LockPage>();
            _isLocked = true;
            page.Unlocked += OnUnlocked;
            await root.Navigation.PushModalAsync(page, animated: false);
        });

    private void OnUnlocked(object? sender, EventArgs e)
    {
        if (sender is LockPage page) page.Unlocked -= OnUnlocked;
        _isLocked = false;
    }

    private static async Task<Page?> WaitForRootPageAsync()
    {
        var deadline = DateTime.UtcNow + ShellWait;
        while (true)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page?.Handler is not null || DateTime.UtcNow > deadline) return page;
            await Task.Delay(100);
        }
    }
}
