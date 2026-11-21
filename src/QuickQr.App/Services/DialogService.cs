using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;

namespace QuickQr.Services;

/// <summary>
/// Dialogs on the top-most visible page (modal first, then the Shell's current page), always marshalled to the
/// main thread. Toasts and snackbars use CommunityToolkit.Maui; where those are unavailable (e.g. Windows without
/// notification support) they degrade to an alert so the message and its action are never lost.
/// </summary>
public sealed class DialogService(ILogger<DialogService> logger) : IDialogService
{
    private static readonly TimeSpan SnackbarDuration = TimeSpan.FromSeconds(4);

    public Task AlertAsync(string title, string message, string cancel = "OK") =>
        OnMainThreadAsync(page => page.DisplayAlertAsync(title, message, cancel));

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel") =>
        OnMainThreadAsync(page => page.DisplayAlertAsync(title, message, accept, cancel), fallback: false);

    public async Task<string?> ActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
    {
        var choice = await OnMainThreadAsync<string?>(
            page => page.DisplayActionSheetAsync(title, cancel, destruction, buttons), fallback: null);
        return choice is null || choice == cancel ? null : choice;
    }

    public Task<string?> PromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel",
        string? placeholder = null, string? initialValue = null, int maxLength = -1, Keyboard? keyboard = null) =>
        OnMainThreadAsync<string?>(
            page => page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard ?? Keyboard.Default, initialValue ?? string.Empty),
            fallback: null);

    public Task ToastAsync(string message, bool longDuration = false) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Toast.Make(message, longDuration ? ToastDuration.Long : ToastDuration.Short, 14).Show();
            }
            catch (Exception ex)
            {
                // Toasts are best effort (unsupported on some desktop configurations).
                logger.LogDebug(ex, "Toast unavailable: {Message}", message);
            }
        });

    public Task SnackbarAsync(string message, string? actionText = null, Func<Task>? action = null) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var hasAction = action is not null && !string.IsNullOrEmpty(actionText);
            try
            {
                if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                    throw new PlatformNotSupportedException("Snackbar requires Windows app notifications.");

                var snackbar = Snackbar.Make(
                    message,
                    hasAction ? () => RunAction(action!) : null,
                    actionText ?? "OK",
                    SnackbarDuration,
                    null,
                    null);
                await snackbar.Show();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Snackbar unavailable, falling back to an alert");
                var page = CurrentPage();
                if (page is null) return;

                if (hasAction)
                {
                    if (await page.DisplayAlertAsync(string.Empty, message, actionText!, "Dismiss"))
                        await action!();
                }
                else
                {
                    await ToastAsync(message);
                }
            }
        });

    private void RunAction(Func<Task> action) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await action(); }
            catch (Exception ex) { logger.LogWarning(ex, "Snackbar action failed"); }
        });

    private Task OnMainThreadAsync(Func<Page, Task> show) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = CurrentPage();
            if (page is null)
            {
                logger.LogWarning("No page available to show a dialog");
                return;
            }

            await show(page);
        });

    private Task<T> OnMainThreadAsync<T>(Func<Page, Task<T>> show, T fallback) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = CurrentPage();
            if (page is null)
            {
                logger.LogWarning("No page available to show a dialog");
                return fallback;
            }

            return await show(page);
        });

    /// <summary>The page the user is looking at: the top modal, else the Shell's current page, else the window page.</summary>
    internal static Page? CurrentPage()
    {
        var root = Application.Current?.Windows.FirstOrDefault(w => w.Page is not null)?.Page;
        if (root is null) return null;

        var modal = root.Navigation.ModalStack.LastOrDefault();
        if (modal is not null) return modal is Shell modalShell ? modalShell.CurrentPage ?? modal : modal;

        return root is Shell shell ? shell.CurrentPage ?? root : root;
    }
}
