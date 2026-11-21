using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using QuickQr.Services;

namespace QuickQr.Mvvm;

/// <summary>
/// Base for every ViewModel: busy state, title and a guarded async runner that logs failures and shows
/// a friendly message instead of crashing. Use partial properties: <c>[ObservableProperty] public partial string Name { get; set; }</c>.
/// </summary>
public abstract partial class ViewModelBase(IDialogService dialogs, ILogger logger) : ObservableObject
{
    protected IDialogService Dialogs { get; } = dialogs;
    protected ILogger Logger { get; } = logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    public bool IsNotBusy => !IsBusy;

    /// <summary>Called by the page in OnAppearing/NavigatedTo. Override for loading.</summary>
    public virtual Task OnAppearingAsync() => Task.CompletedTask;

    /// <summary>Called by the page in OnDisappearing/NavigatedFrom.</summary>
    public virtual Task OnDisappearingAsync() => Task.CompletedTask;

    /// <summary>Runs <paramref name="operation"/> with IsBusy set; exceptions are logged and surfaced as an alert.</summary>
    protected async Task RunBusyAsync(Func<Task> operation, string? errorTitle = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            // User cancelled: nothing to report.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Operation failed in {ViewModel}", GetType().Name);
            await Dialogs.AlertAsync(errorTitle ?? "Something went wrong", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
