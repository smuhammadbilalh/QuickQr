using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickQr.Core.Barcodes;
using QuickQr.Core.History;
using QuickQr.Services.Presentation;

namespace QuickQr.Features.History;

/// <summary>One history row. Commands call back into the list view model.</summary>
public sealed partial class HistoryItemViewModel : ObservableObject
{
    public HistoryItemViewModel(
        HistoryEntry entry,
        DateTimeOffset now,
        Func<HistoryItemViewModel, Task> open,
        Func<HistoryItemViewModel, Task> toggleFavorite,
        Func<HistoryItemViewModel, Task> delete)
    {
        Entry = entry;
        IsFavorite = entry.IsFavorite;
        UpdateTime(now);
        OpenCommand = new AsyncRelayCommand(() => open(this));
        ToggleFavoriteCommand = new AsyncRelayCommand(() => toggleFavorite(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public HistoryEntry Entry { get; private set; }
    public Guid Id => Entry.Id;
    public string Glyph => Glyphs.For(Entry.ContentType);
    public string Title => string.IsNullOrWhiteSpace(Entry.Title) ? Entry.Payload : Entry.Title;
    public string? Subtitle => Entry.Subtitle;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Entry.Subtitle);
    public string KindText => Entry.Kind == HistoryKind.Generated ? "Created" : "Scanned";
    public string FormatText => Entry.Format is BarcodeFormat.Unknown or BarcodeFormat.QrCode ? string.Empty : " · " + Entry.Format.DisplayName();

    [ObservableProperty]
    public partial string Meta { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StarGlyph), nameof(StarDescription))]
    public partial bool IsFavorite { get; set; }

    public string StarGlyph => IsFavorite ? Glyphs.Star : Glyphs.StarOutline;
    public string StarDescription => IsFavorite ? "Remove from favourites" : "Add to favourites";

    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    public void UpdateTime(DateTimeOffset now) =>
        Meta = $"{KindText}{FormatText} · {RelativeTime.Format(Entry.CreatedAt, now)}";

    public void SetFavorite(bool value)
    {
        Entry = Entry with { IsFavorite = value };
        IsFavorite = value;
    }
}
