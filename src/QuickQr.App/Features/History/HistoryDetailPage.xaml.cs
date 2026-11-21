using Microsoft.Extensions.Logging;

namespace QuickQr.Features.History;

public partial class HistoryDetailPage : ContentPage
{
    private readonly HistoryDetailViewModel _viewModel;
    private readonly ILogger<HistoryDetailPage> _logger;

    public HistoryDetailPage(HistoryDetailViewModel viewModel, ILogger<HistoryDetailPage> logger)
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
            _logger.LogError(ex, "{Page} failed to load", nameof(HistoryDetailPage));
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
            _logger.LogError(ex, "{Page} failed to unload", nameof(HistoryDetailPage));
        }
    }
}