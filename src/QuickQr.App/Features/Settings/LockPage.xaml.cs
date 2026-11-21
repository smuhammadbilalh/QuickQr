using Microsoft.Extensions.Logging;

namespace QuickQr.Features.Settings;

/// <summary>Modal lock screen pushed by <see cref="Services.Lifecycle.AppLockLifecycleHook"/>; pops itself once unlocked.</summary>
public partial class LockPage : ContentPage
{
    private readonly LockViewModel _viewModel;
    private readonly ILogger<LockPage> _logger;
    private bool _closing;

    public LockPage(LockViewModel viewModel, ILogger<LockPage> logger)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _logger = logger;
        _viewModel.Unlocked += OnUnlocked;
    }

    /// <summary>Raised after the page has been dismissed.</summary>
    public event EventHandler? Unlocked;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lock screen failed to prompt");
        }
    }

    // The lock can't be dismissed with the back button.
    protected override bool OnBackButtonPressed() => true;

    private async void OnUnlocked(object? sender, EventArgs e)
    {
        if (_closing) return;
        _closing = true;
        _viewModel.Unlocked -= OnUnlocked;
        try
        {
            if (Navigation.ModalStack.Contains(this))
                await Navigation.PopModalAsync(animated: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Closing the lock screen failed");
        }
        finally
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
    }
}
