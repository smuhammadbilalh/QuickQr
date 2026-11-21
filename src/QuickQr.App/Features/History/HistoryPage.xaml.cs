using Microsoft.Extensions.Logging;

namespace QuickQr.Features.History;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;
    private readonly ILogger<HistoryPage> _logger;

    public HistoryPage(HistoryViewModel viewModel, ILogger<HistoryPage> logger)
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
            _logger.LogError(ex, "{Page} failed to load", nameof(HistoryPage));
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
            _logger.LogError(ex, "{Page} failed to unload", nameof(HistoryPage));
        }
    }
}