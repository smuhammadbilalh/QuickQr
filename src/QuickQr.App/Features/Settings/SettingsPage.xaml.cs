using Microsoft.Extensions.Logging;

namespace QuickQr.Features.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly ILogger<SettingsPage> _logger;

    public SettingsPage(SettingsViewModel viewModel, ILogger<SettingsPage> logger)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Page} failed to load", nameof(SettingsPage));
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
            await _viewModel.OnDisappearingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Page} failed to unload", nameof(SettingsPage));
        }
    }
}