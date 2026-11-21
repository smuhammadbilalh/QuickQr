namespace QuickQr.Controls;

/// <summary>Swatches plus hex entry. <see cref="Color"/> is a normalised hex string and binds two-way by default.</summary>
public partial class ColorSwatchPicker : ContentView
{
    public static IReadOnlyList<string> DefaultSwatches { get; } = new[]
    {
        "#000000", "#0F172A", "#374151", "#4F46E5", "#7C3AED", "#DB2777", "#DC2626", "#EA580C",
        "#CA8A04", "#16A34A", "#0891B2", "#2563EB", "#FFFFFF", "#F1F5F9", "#FEF3C7", "#ECFEFF"
    };

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(ColorSwatchPicker), string.Empty,
        propertyChanged: (b, _, n) => ((ColorSwatchPicker)b).OnTitleChanged((string?)n));

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(string), typeof(ColorSwatchPicker), "#000000", BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((ColorSwatchPicker)b).OnColorChanged((string?)n));

    public static readonly BindableProperty SwatchesProperty = BindableProperty.Create(
        nameof(Swatches), typeof(IReadOnlyList<string>), typeof(ColorSwatchPicker), null,
        propertyChanged: (b, _, n) => ((ColorSwatchPicker)b).OnSwatchesChanged(n as IReadOnlyList<string>));

    private bool _updatingEntry;

    public ColorSwatchPicker()
    {
        InitializeComponent();
        OnSwatchesChanged(null);
        OnColorChanged(Color);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public IReadOnlyList<string>? Swatches
    {
        get => (IReadOnlyList<string>?)GetValue(SwatchesProperty);
        set => SetValue(SwatchesProperty, value);
    }

    private void OnTitleChanged(string? title)
    {
        TitleLabel.Text = title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(title);
        SemanticProperties.SetDescription(HexEntry, string.IsNullOrEmpty(title) ? "Hex colour" : $"{title} hex colour");
    }

    private void OnSwatchesChanged(IReadOnlyList<string>? swatches) =>
        BindableLayout.SetItemsSource(SwatchLayout, swatches ?? DefaultSwatches);

    private void OnColorChanged(string? hex)
    {
        CurrentChip.BackgroundColor = HexColor.TryParse(hex, out var a, out var r, out var g, out var b)
            ? Microsoft.Maui.Graphics.Color.FromRgba(r, g, b, a)
            : Colors.Transparent;

        // Keep the user's in-progress text while it is still equivalent to the bound colour.
        if (HexColor.TryNormalize(HexEntry.Text, out var current) && string.Equals(current, hex, StringComparison.OrdinalIgnoreCase))
            return;

        _updatingEntry = true;
        HexEntry.Text = hex;
        _updatingEntry = false;
    }

    private void OnSwatchTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: string hex })
            Color = hex;
    }

    private void OnHexTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingEntry) return;
        if (HexColor.TryNormalize(e.NewTextValue, out var normalized) && !string.Equals(normalized, Color, StringComparison.OrdinalIgnoreCase))
            Color = normalized;
    }

    private void OnHexUnfocused(object? sender, FocusEventArgs e)
    {
        // Snap invalid or partial input back to the current colour.
        if (!HexColor.TryNormalize(HexEntry.Text, out _))
        {
            _updatingEntry = true;
            HexEntry.Text = Color;
            _updatingEntry = false;
        }
    }
}
