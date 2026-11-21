using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickQr.Controls;

/// <summary>One selectable choice in an <see cref="OptionGroup"/> (rendered as a chip or tile).</summary>
public sealed partial class OptionItem : ObservableObject
{
    internal OptionItem(OptionGroup owner, object? value, string label, string? glyph, string? caption)
    {
        Value = value;
        Label = label;
        Glyph = glyph;
        Caption = caption;
        SelectCommand = new RelayCommand(() => owner.Select(this));
    }

    public object? Value { get; }
    public string Label { get; }
    public string? Glyph { get; }
    public bool HasGlyph => !string.IsNullOrEmpty(Glyph);
    public string? Caption { get; }
    public bool HasCaption => !string.IsNullOrEmpty(Caption);
    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemanticDescription))]
    public partial bool IsSelected { get; set; }

    /// <summary>True when the option needs a paid tier. Locked options stay selectable (try before you buy).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemanticDescription))]
    public partial bool IsLocked { get; set; }

    public string SemanticDescription =>
        Label + (IsLocked ? ", premium feature" : string.Empty) + (IsSelected ? ", selected" : string.Empty);
}

/// <summary>Single-selection list of options, independent of any view. Bind it to an <see cref="OptionChipGroup"/>.</summary>
public partial class OptionGroup : ObservableObject
{
    public ObservableCollection<OptionItem> Items { get; } = [];

    [ObservableProperty]
    public partial OptionItem? SelectedItem { get; private set; }

    /// <summary>Raised after the selection changed (by the user or programmatically).</summary>
    public event EventHandler? SelectionChanged;

    public void Select(OptionItem? item)
    {
        if (item is null || ReferenceEquals(item, SelectedItem) || !Items.Contains(item)) return;
        if (SelectedItem is not null) SelectedItem.IsSelected = false;
        item.IsSelected = true;
        SelectedItem = item;
        OnSelectionChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnSelectionChanged()
    {
    }

    protected OptionItem AddItem(object? value, string label, string? glyph, string? caption)
    {
        var item = new OptionItem(this, value, label, glyph, caption);
        Items.Add(item);
        if (SelectedItem is null)
        {
            item.IsSelected = true;
            SelectedItem = item;
        }

        return item;
    }

    public void Clear()
    {
        Items.Clear();
        SelectedItem = null;
    }
}

/// <summary>Typed <see cref="OptionGroup"/>: <see cref="Value"/> reads or selects by value.</summary>
public sealed partial class OptionGroup<T> : OptionGroup
{
    public OptionGroup<T> Add(T value, string label, string? glyph = null, string? caption = null)
    {
        AddItem(value, label, glyph, caption);
        return this;
    }

    /// <summary>The selected value; setting it selects the matching option (ignored when absent).</summary>
    public T Value
    {
        get => SelectedItem?.Value is T value ? value : default!;
        set => Select(Find(value));
    }

    public OptionItem? Find(T value) =>
        Items.FirstOrDefault(i => i.Value is T v && EqualityComparer<T>.Default.Equals(v, value));

    public bool Contains(T value) => Find(value) is not null;

    /// <summary>Updates every option's lock flag.</summary>
    public void SetLocked(Func<T, bool> isLocked)
    {
        foreach (var item in Items)
            item.IsLocked = item.Value is T v && isLocked(v);
    }

    protected override void OnSelectionChanged() => OnPropertyChanged(nameof(Value));
}
