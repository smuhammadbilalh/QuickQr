namespace QuickQr.Features.Premium;

public partial class PaywallPage : ContentPage
{
    private readonly PaywallViewModel _viewModel;

    public PaywallPage(PaywallViewModel viewModel)
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
