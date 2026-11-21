namespace QuickQr.Features.About;

public partial class LicensesPage : ContentPage
{
    public LicensesPage(LicensesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
