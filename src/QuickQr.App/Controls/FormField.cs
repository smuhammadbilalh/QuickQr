namespace QuickQr.Controls;

/// <summary>
/// Labelled input used by every form: caption, a soft input container and an optional hint.
/// <see cref="Text"/> binds two-way. Values are synchronised without reflection bindings (trim/AOT safe).
/// </summary>
public abstract class FormFieldBase<TInput> : ContentView where TInput : InputView, new()
{
    private readonly Label _label;
    private readonly Label _hint;
    private bool _syncing;

    protected FormFieldBase()
    {
        _label = new Label { FontAttributes = FontAttributes.Bold, IsVisible = false };
        _label.SetDynamicResource(StyleProperty, "Caption");

        Input = new TInput();
        Input.TextChanged += OnInputTextChanged;

        var container = new Border { Content = Input, Padding = new Thickness(12, 0) };
        container.SetDynamicResource(StyleProperty, "CardAlt");

        _hint = new Label { IsVisible = false };
        _hint.SetDynamicResource(StyleProperty, "Caption");

        Content = new VerticalStackLayout { Spacing = 6, Children = { _label, container, _hint } };
    }

    protected TInput Input { get; }

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(FormFieldBase<TInput>), string.Empty,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).OnLabelChanged((string?)n));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(FormFieldBase<TInput>), string.Empty, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).OnTextChanged((string?)n));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(FormFieldBase<TInput>), string.Empty,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).Input.Placeholder = (string?)n);

    public static readonly BindableProperty HintProperty = BindableProperty.Create(
        nameof(Hint), typeof(string), typeof(FormFieldBase<TInput>), null,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).OnHintChanged((string?)n));

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard), typeof(Keyboard), typeof(FormFieldBase<TInput>), Keyboard.Default,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).Input.Keyboard = (Keyboard)n);

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(FormFieldBase<TInput>), int.MaxValue,
        propertyChanged: (b, _, n) => ((FormFieldBase<TInput>)b).Input.MaxLength = (int)n);

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    private void OnLabelChanged(string? label)
    {
        _label.Text = label;
        _label.IsVisible = !string.IsNullOrEmpty(label);
        SemanticProperties.SetDescription(Input, label);
    }

    private void OnHintChanged(string? hint)
    {
        _hint.Text = hint;
        _hint.IsVisible = !string.IsNullOrEmpty(hint);
        SemanticProperties.SetHint(Input, hint);
    }

    private void OnTextChanged(string? text)
    {
        if (_syncing || Input.Text == text) return;
        _syncing = true;
        Input.Text = text;
        _syncing = false;
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Text = e.NewTextValue;
        _syncing = false;
    }
}

/// <summary>Single-line labelled entry.</summary>
public sealed class FormField : FormFieldBase<Entry>
{
    public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(
        nameof(IsPassword), typeof(bool), typeof(FormField), false,
        propertyChanged: (b, _, n) => ((FormField)b).Input.IsPassword = (bool)n);

    public FormField()
    {
        Input.ClearButtonVisibility = ClearButtonVisibility.WhileEditing;
    }

    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }
}

/// <summary>Multi-line labelled editor that grows with its text.</summary>
public sealed class FormEditorField : FormFieldBase<Editor>
{
    public FormEditorField()
    {
        Input.AutoSize = EditorAutoSizeOption.TextChanges;
        Input.MinimumHeightRequest = 96;
    }
}
