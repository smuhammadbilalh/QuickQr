namespace QuickQr.Controls;

/// <summary>Renders an <see cref="OptionGroup"/> as wrapping chips. Selection logic lives in the group.</summary>
public partial class OptionChipGroup : ContentView
{
    public static readonly BindableProperty GroupProperty = BindableProperty.Create(
        nameof(Group), typeof(OptionGroup), typeof(OptionChipGroup),
        propertyChanged: (bindable, _, newValue) => ((OptionChipGroup)bindable).ChipsLayout.BindingContext = newValue);

    public OptionChipGroup()
    {
        InitializeComponent();
    }

    public OptionGroup? Group
    {
        get => (OptionGroup?)GetValue(GroupProperty);
        set => SetValue(GroupProperty, value);
    }
}
