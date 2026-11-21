using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Settings;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.Settings;

/// <summary>Lock screen shown by the app-lock lifecycle hook. Prompts once automatically, then on demand.</summary>
public sealed partial class LockViewModel(
    IDialogService dialogs,
    ILogger<LockViewModel> logger,
    IAppLockService appLock,
    IAppSettingsService settings) : ViewModelBase(dialogs, logger)
{
    private bool _autoPrompted;

    /// <summary>Raised once the user has authenticated (or the lock had to be lifted because the device lock was removed).</summary>
    public event EventHandler? Unlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public override async Task OnAppearingAsync()
    {
        if (_autoPrompted) return;
        _autoPrompted = true;
        await UnlockAsync();
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!await appLock.IsAvailableAsync())
            {
                // The device no longer has a screen lock: never lock the user out of their own data.
                await settings.SaveAsync(settings.Current with { AppLockEnabled = false });
                await Dialogs.AlertAsync("App lock turned off",
                    "This device no longer has a screen lock, so QuickQr's app lock was switched off. Set a screen lock and turn it on again in Settings.");
                Unlocked?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (await appLock.AuthenticateAsync("Unlock QuickQr to continue"))
            {
                Message = null;
                Unlocked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Message = "QuickQr is still locked. Tap Unlock to try again.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unlock failed");
            Message = "Unlocking didn't work. Tap Unlock to try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
