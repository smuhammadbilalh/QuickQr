namespace QuickQr.Controls;

/// <summary>Small gradient pill ("PRO" with a lock) marking a premium option. Bind <see cref="VisualElement.IsVisible"/> to the lock state.</summary>
public sealed class PremiumLockBadge : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(PremiumLockBadge), "PRO",
        propertyChanged: (b, _, n) => ((PremiumLockBadge)b).OnTextChanged((string?)n));

    private readonly Label _text;

    public PremiumLockBadge()
    {
        var glyph = new Label
        {
            Text = "\ue899", // lock
            FontFamily = "MaterialIcons",
            FontSize = 11,
            TextColor = Colors.White,
            VerticalTextAlignment = TextAlignment.Center,
        };
        _text = new Label
        {
            Text = Text,
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 0.5,
            TextColor = Colors.White,
            VerticalTextAlignment = TextAlignment.Center,
        };

        var pill = new Border { Content = new HorizontalStackLayout { Spacing = 3, Children = { glyph, _text } } };
        pill.SetDynamicResource(StyleProperty, "PremiumBadge");

        Content = pill;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(this, "Premium feature");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void OnTextChanged(string? text)
    {
        _text.Text = text;
        SemanticProperties.SetDescription(this, $"{text} feature");
    }
}
