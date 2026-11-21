using BarcodeScanning;
using Microsoft.Extensions.Logging;

namespace QuickQr.Features.Scan;

public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _viewModel;
    private readonly ILogger<ScanPage> _logger;

    public ScanPage(ScanViewModel viewModel, ILogger<ScanPage> logger)
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
            _logger.LogError(ex, "Starting the scanner failed");
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
            _logger.LogError(ex, "Stopping the scanner failed");
        }
    }

    /// <summary>Camera plumbing: library results → <see cref="DetectedCode"/> → view model, on the UI thread.</summary>
    private void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var codes = ScannerFormatMap.ToDetected(e.BarcodeResults);
        if (codes.Count == 0) return;

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await _viewModel.OnCodesDetectedAsync(codes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling a detection failed");
            }
        });
    }

    protected override bool OnBackButtonPressed()
    {
        // Close an open sheet before leaving the page.
        if (_viewModel.HasResult)
        {
            _viewModel.DismissResultCommand.Execute(null);
            return true;
        }

        if (_viewModel.IsSessionListOpen)
        {
            _viewModel.ToggleSessionListCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
