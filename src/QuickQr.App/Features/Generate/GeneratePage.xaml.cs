namespace QuickQr.Features.Generate;

public partial class GeneratePage : ContentPage
{
    /// <summary>Window width from which the preview sits beside the form instead of between its sections.</summary>
    private const double WideLayoutWidth = 840;
    private const double SidePanelWidth = 420;
    private const int PreviewIndexInStack = 2;

    private readonly GenerateViewModel _viewModel;
    private bool? _isWide;

    public GeneratePage(GenerateViewModel viewModel)
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

    // Layout only: moves the preview panel between the single column (phones) and a side column (desktop/tablet).
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;

        var wide = width >= WideLayoutWidth;
        if (_isWide == wide) return;
        _isWide = wide;

        if (wide)
        {
            MainStack.Remove(PreviewPanel);
            if (!SideStack.Contains(PreviewPanel)) SideStack.Add(PreviewPanel);
            RootGrid.ColumnDefinitions[1].Width = new GridLength(SidePanelWidth);
            SideScroll.IsVisible = true;
        }
        else
        {
            SideStack.Remove(PreviewPanel);
            if (!MainStack.Contains(PreviewPanel))
                MainStack.Insert(Math.Min(PreviewIndexInStack, MainStack.Count), PreviewPanel);
            RootGrid.ColumnDefinitions[1].Width = new GridLength(0);
            SideScroll.IsVisible = false;
        }
    }
}
