namespace QuickQr.Features.Batch;

public partial class BatchPage : ContentPage
{
    private readonly BatchViewModel _viewModel;

    public BatchPage(BatchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.OnDisappearingAsync();
    }
}
