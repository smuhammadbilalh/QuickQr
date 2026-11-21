using Microsoft.Extensions.Logging;

namespace QuickQr.Features.About;

public partial class AboutPage : ContentPage
{
    private readonly AboutViewModel _viewModel;
    private readonly ILogger<AboutPage> _logger;

    public AboutPage(AboutViewModel viewModel, ILogger<AboutPage> logger)
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
            _logger.LogError(ex, "{Page} failed to load", nameof(AboutPage));
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
            _logger.LogError(ex, "{Page} failed to unload", nameof(AboutPage));
        }
    }
}