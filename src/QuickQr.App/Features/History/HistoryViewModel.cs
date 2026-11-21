using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.History;

public enum HistorySegment { All = 0, Scanned = 1, Created = 2 }

/// <summary>
/// History tab: segments, debounced search, favourites filter and incremental loading (pages of
/// <see cref="PageSize"/>). Deletes are optimistic with a snackbar "Undo"; the real delete runs after
/// <see cref="UndoWindow"/> unless undone.
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    internal const int PageSize = 50;
    internal static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan OwnChangeQuietPeriod = TimeSpan.FromSeconds(1.5);

    private readonly IHistoryService _history;
    private readonly INavigationService _navigation;
    private readonly IPremiumGate _premium;
    private readonly IFileExportService _files;
    private readonly IShareService _share;
    private readonly IHapticsService _haptics;
    private readonly TimeProvider _clock;

    private readonly HashSet<Guid> _pendingDeletes = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _debounceCts;
    private int _skip;
    private bool _hasMore;
    private bool _loadingMore;
    private bool _isVisible;
    private bool _stale = true;
    private bool _subscribed;
    private DateTimeOffset _ignoreChangesUntil = DateTimeOffset.MinValue;

    public HistoryViewModel(
        IDialogService dialogs,
        ILogger<HistoryViewModel> logger,
        IHistoryService history,
        INavigationService navigation,
        IPremiumGate premium,
        IFileExportService files,
        IShareService share,
        IHapticsService haptics,
        TimeProvider clock) : base(dialogs, logger)
    {
        _history = history;
        _navigation = navigation;
        _premium = premium;
        _files = files;
        _share = share;
        _haptics = haptics;
        _clock = clock;
        Title = "History";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllSelected), nameof(IsScannedSelected), nameof(IsCreatedSelected), nameof(EmptyTitle), nameof(EmptyMessage))]
    public partial HistorySegment Segment { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyTitle), nameof(EmptyMessage))]
    public partial bool FavoritesOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowList))]
    public partial ObservableCollection<HistoryItemViewModel> Items { get; set; } = [];

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowList))]
    public partial bool HasLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowList))]
    public partial int ItemCount { get; set; }

    public bool IsAllSelected => Segment == HistorySegment.All;
    public bool IsScannedSelected => Segment == HistorySegment.Scanned;
    public bool IsCreatedSelected => Segment == HistorySegment.Created;
    public bool IsExportLocked => !_premium.IsUnlocked(PremiumFeature.HistoryExport);

    public bool ShowEmpty => HasLoaded && ItemCount == 0;
    public bool ShowList => !ShowEmpty;
    public bool IsFiltered => FavoritesOnly || !string.IsNullOrWhiteSpace(SearchText);

    public string EmptyTitle => IsFiltered
        ? "No matches"
        : Segment switch
        {
            HistorySegment.Scanned => "Nothing scanned yet",
            HistorySegment.Created => "Nothing created yet",
            _ => "Your history is empty",
        };

    public string EmptyMessage => IsFiltered
        ? (FavoritesOnly && string.IsNullOrWhiteSpace(SearchText)
            ? "Tap the star on any code to keep it in your favourites."
            : "Try a different search or clear the filters.")
        : "Codes you scan or create appear here, stored only on this device.";

    // ---------- Lifecycle ----------

    public override async Task OnAppearingAsync()
    {
        _isVisible = true;
        if (!_subscribed)
        {
            _subscribed = true;
            _history.HistoryChanged += OnHistoryChanged;
        }

        OnPropertyChanged(nameof(IsExportLocked));
        if (_stale)
        {
            await ReloadAsync();
        }
        else
        {
            var now = _clock.GetUtcNow();
            foreach (var item in Items) item.UpdateTime(now);
        }
    }

    public override Task OnDisappearingAsync()
    {
        _isVisible = false;
        return Task.CompletedTask;
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_clock.GetUtcNow() < _ignoreChangesUntil) return;
        _stale = true;
        if (!_isVisible) return;
        MainThread.BeginInvokeOnMainThread(() => ScheduleReload(SearchDebounce));
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        ScheduleReload(SearchDebounce);
    }

    partial void OnSegmentChanged(HistorySegment value) => _ = ReloadAsync();

    partial void OnFavoritesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFiltered));
        _ = ReloadAsync();
    }

    // ---------- Loading ----------

    private HistoryQuery BuildQuery(int skip, int take) => new(
        Kind: Segment switch
        {
            HistorySegment.Scanned => HistoryKind.Scanned,
            HistorySegment.Created => HistoryKind.Generated,
            _ => null,
        },
        Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        FavoritesOnly: FavoritesOnly,
        Skip: skip,
        Take: take);

    private void ScheduleReload(TimeSpan delay)
    {
        _debounceCts?.Cancel();
        var cts = _debounceCts = new CancellationTokenSource();
        _ = DebouncedReloadAsync(delay, cts.Token);
    }

    private async Task DebouncedReloadAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        try
        {
            var page = await _history.QueryAsync(BuildQuery(0, PageSize), cts.Token);
            if (cts.IsCancellationRequested) return;

            _stale = false;
            _skip = page.Count;
            _hasMore = page.Count == PageSize;
            var now = _clock.GetUtcNow();
            Items = new ObservableCollection<HistoryItemViewModel>(
                page.Where(e => !_pendingDeletes.Contains(e.Id)).Select(e => CreateItem(e, now)));
            ItemCount = Items.Count;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Loading history failed");
            await Dialogs.AlertAsync("Couldn't load history", "Please try again.");
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsRefreshing = false;
                HasLoaded = true;
            }
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_loadingMore || !_hasMore || !HasLoaded) return;
        _loadingMore = true;
        var cts = _loadCts;
        try
        {
            var page = await _history.QueryAsync(BuildQuery(_skip, PageSize), cts?.Token ?? CancellationToken.None);
            if (cts is not null && (cts.IsCancellationRequested || !ReferenceEquals(cts, _loadCts))) return;

            _skip += page.Count;
            _hasMore = page.Count == PageSize;
            var now = _clock.GetUtcNow();
            var known = Items.Select(i => i.Id).ToHashSet();
            foreach (var entry in page.Where(e => !known.Contains(e.Id) && !_pendingDeletes.Contains(e.Id)))
                Items.Add(CreateItem(entry, now));
            ItemCount = Items.Count;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Loading more history failed");
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private HistoryItemViewModel CreateItem(HistoryEntry entry, DateTimeOffset now) =>
        new(entry, now, OpenAsync, ToggleFavoriteAsync, DeleteAsync);

    // ---------- Filters ----------

    [RelayCommand]
    private void SelectSegment(string? segment)
    {
        if (Enum.TryParse<HistorySegment>(segment, ignoreCase: true, out var value))
            Segment = value;
    }

    [RelayCommand]
    private void ToggleFavoritesOnly() => FavoritesOnly = !FavoritesOnly;

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        FavoritesOnly = false;
    }

    [RelayCommand]
    private Task GoToScanAsync() => _navigation.GoToTabAsync(Routes.Scan);

    [RelayCommand]
    private Task GoToCreateAsync() => _navigation.GoToTabAsync(Routes.Create);

    // ---------- Item actions ----------

    private Task OpenAsync(HistoryItemViewModel item) =>
        _navigation.GoToAsync(Routes.HistoryDetail, new Dictionary<string, object> { [Routes.IdParameter] = item.Id.ToString() });

    private async Task ToggleFavoriteAsync(HistoryItemViewModel item)
    {
        var value = !item.IsFavorite;
        item.SetFavorite(value);
        _haptics.Click();
        try
        {
            MarkOwnChange();
            await _history.SetFavoriteAsync(item.Id, value);
            if (FavoritesOnly && !value)
            {
                Items.Remove(item);
                ItemCount = Items.Count;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Updating favourite failed");
            item.SetFavorite(!value);
            await Dialogs.AlertAsync("Couldn't update favourite", "Please try again.");
        }
    }

    private async Task DeleteAsync(HistoryItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index < 0 || !_pendingDeletes.Add(item.Id)) return;

        Items.RemoveAt(index);
        ItemCount = Items.Count;
        _haptics.Click();

        var undone = false;
        var snackbar = Dialogs.SnackbarAsync("Deleted from history", "Undo", () =>
        {
            undone = true;
            _pendingDeletes.Remove(item.Id);
            if (!Items.Contains(item))
            {
                Items.Insert(Math.Min(index, Items.Count), item);
                ItemCount = Items.Count;
            }

            return Task.CompletedTask;
        });

        try
        {
            await Task.WhenAll(Task.Delay(UndoWindow), snackbar);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Undo snackbar failed");
        }

        if (undone) return;

        try
        {
            MarkOwnChange();
            await _history.DeleteAsync(item.Id);
            _skip = Math.Max(0, _skip - 1);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Deleting a history entry failed");
            if (!Items.Contains(item))
            {
                Items.Insert(Math.Min(index, Items.Count), item);
                ItemCount = Items.Count;
            }

            await Dialogs.AlertAsync("Couldn't delete", "The entry could not be deleted. Please try again.");
        }
        finally
        {
            _pendingDeletes.Remove(item.Id);
        }
    }

    private void MarkOwnChange() => _ignoreChangesUntil = _clock.GetUtcNow() + OwnChangeQuietPeriod;

    // ---------- Export / clear ----------

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!await _premium.EnsureAsync(PremiumFeature.HistoryExport))
        {
            OnPropertyChanged(nameof(IsExportLocked));
            return;
        }

        OnPropertyChanged(nameof(IsExportLocked));
        const string csv = "CSV (spreadsheet)";
        const string json = "JSON";
        var choice = await Dialogs.ActionSheetAsync("Export history", "Cancel", null, csv, json);
        if (choice is null) return;
        var format = choice == json ? HistoryExportFormat.Json : HistoryExportFormat.Csv;

        await RunBusyAsync(async () =>
        {
            using var output = new MemoryStream();
            var result = await _history.ExportAsync(BuildQuery(0, int.MaxValue), format, output);
            if (result.IsFailure)
            {
                await Dialogs.AlertAsync("Export failed", result.Error.Message);
                return;
            }

            var extension = format == HistoryExportFormat.Json ? ".json" : ".csv";
            var mime = format == HistoryExportFormat.Json ? "application/json" : "text/csv";
            var path = await _files.WriteTempFileAsync(output.ToArray(), $"quickqr-history-{_clock.GetLocalNow():yyyyMMdd-HHmm}{extension}");
            await _share.ShareFileAsync("QuickQr history", path, mime);
        }, "Export failed");
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var kind = Segment switch
        {
            HistorySegment.Scanned => HistoryKind.Scanned,
            HistorySegment.Created => HistoryKind.Generated,
            _ => (HistoryKind?)null,
        };

        var count = await _history.CountAsync(kind);
        if (count == 0)
        {
            await Dialogs.ToastAsync("Nothing to clear");
            return;
        }

        var what = kind switch
        {
            HistoryKind.Scanned => "scanned codes",
            HistoryKind.Generated => "created codes",
            _ => "codes in your history",
        };
        if (!await Dialogs.ConfirmAsync("Clear history?",
                $"Permanently delete all {count} {what}, including favourites? This can't be undone.", "Delete", "Cancel"))
            return;

        await RunBusyAsync(async () =>
        {
            MarkOwnChange();
            await _history.ClearAsync(kind);
            await ReloadAsync();
            await Dialogs.ToastAsync("History cleared");
        }, "Couldn't clear history");
    }
}
