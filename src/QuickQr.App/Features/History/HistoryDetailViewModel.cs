using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Generation;
using QuickQr.Core.History;
using QuickQr.Core.Rendering;
using QuickQr.Core.Serialization;
using QuickQr.Core.Settings;
using QuickQr.Features.Scan;
using QuickQr.Mvvm;
using QuickQr.Services;
using QuickQr.Services.Presentation;

namespace QuickQr.Features.History;

/// <summary>
/// One history entry: the regenerated code (preview render, original style for created codes), payload, actions,
/// notes/tags, favourite, share/save, "Edit &amp; re-create" and delete. Receives <see cref="Routes.IdParameter"/>.
/// </summary>
public sealed partial class HistoryDetailViewModel : ViewModelBase, IQueryAttributable
{
    private const int PreviewSize = 768;

    private readonly IHistoryService _history;
    private readonly IQrGenerationService _generation;
    private readonly IContentParser _parser;
    private readonly IContentSummarizer _summarizer;
    private readonly IContentActionProvider _actionProvider;
    private readonly IContentActionExecutor _executor;
    private readonly IShareService _share;
    private readonly IFileExportService _files;
    private readonly IClipboardService _clipboard;
    private readonly INavigationService _navigation;
    private readonly IAppSettingsService _settings;
    private readonly IHapticsService _haptics;

    private Guid _id;
    private byte[]? _imageBytes;
    private string _savedNotes = string.Empty;
    private string _savedTags = string.Empty;
    private CancellationTokenSource? _loadCts;

    public HistoryDetailViewModel(
        IDialogService dialogs,
        ILogger<HistoryDetailViewModel> logger,
        IHistoryService history,
        IQrGenerationService generation,
        IContentParser parser,
        IContentSummarizer summarizer,
        IContentActionProvider actionProvider,
        IContentActionExecutor executor,
        IShareService share,
        IFileExportService files,
        IClipboardService clipboard,
        INavigationService navigation,
        IAppSettingsService settings,
        IHapticsService haptics) : base(dialogs, logger)
    {
        _history = history;
        _generation = generation;
        _parser = parser;
        _summarizer = summarizer;
        _actionProvider = actionProvider;
        _executor = executor;
        _share = share;
        _files = files;
        _clipboard = clipboard;
        _navigation = navigation;
        _settings = settings;
        _haptics = haptics;
        Title = "Details";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntry), nameof(IsGenerated), nameof(SourceText), nameof(CreatedText), nameof(FormatName))]
    public partial HistoryEntry? Entry { get; set; }

    [ObservableProperty]
    public partial QrContent? Content { get; set; }

    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    public partial string? Subtitle { get; set; }

    [ObservableProperty]
    public partial string Glyph { get; set; } = Glyphs.QrCode;

    [ObservableProperty]
    public partial string TypeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Payload { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial ImageSource? CodeImage { get; set; }

    [ObservableProperty]
    public partial bool IsImageLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImageError))]
    public partial string? ImageError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StarGlyph), nameof(StarFontFamily), nameof(FavoriteText))]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedNotes))]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedNotes))]
    public partial string Tags { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrimaryAction))]
    public partial ActionItem? PrimaryAction { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ActionItem> SecondaryActions { get; set; } = [];

    public bool HasEntry => Entry is not null;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool HasImage => CodeImage is not null;
    public bool HasImageError => !string.IsNullOrEmpty(ImageError);
    public bool HasPrimaryAction => PrimaryAction is not null;
    public bool IsGenerated => Entry?.Kind == HistoryKind.Generated;
    public bool HasUnsavedNotes => !string.Equals(Notes.Trim(), _savedNotes, StringComparison.Ordinal) ||
                                   !string.Equals(Tags.Trim(), _savedTags, StringComparison.Ordinal);
    public string StarGlyph => IsFavorite ? Glyphs.Star : Glyphs.StarOutline;

    /// <summary>Material Symbols draws outline and filled stars with the same codepoint; the filled subset font marks favourites.</summary>
    public string StarFontFamily => IsFavorite ? "MaterialIconsFilled" : "MaterialIcons";
    public string FavoriteText => IsFavorite ? "Favourite" : "Add to favourites";

    public string FormatName => Entry is null || Entry.Format == BarcodeFormat.Unknown ? "Text" : Entry.Format.DisplayName();

    public string CreatedText => Entry is null
        ? string.Empty
        : Entry.CreatedAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    public string SourceText => Entry switch
    {
        null => string.Empty,
        { Kind: HistoryKind.Generated } => "Created in QuickQr",
        { Source: ScanSource.Camera } => "Scanned with the camera",
        { Source: ScanSource.Image } => "Scanned from an image",
        { Source: ScanSource.Clipboard } => "Pasted or shared text",
        { Source: ScanSource.Batch } => "Bulk generation",
        _ => "Scanned",
    };

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(Routes.IdParameter, out var value)) return;

        var id = value switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => Guid.Empty,
        };

        if (id == Guid.Empty || id == _id) return;
        _id = id;
        _ = LoadAsync(id);
    }

    private async Task LoadAsync(Guid id)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var entry = await _history.GetAsync(id, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (entry is null)
            {
                await Dialogs.AlertAsync("Not found", "This entry was deleted.");
                await _navigation.GoBackAsync();
                return;
            }

            Apply(entry);
            IsBusy = false;
            await RenderAsync(entry, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Loading history entry failed");
            await Dialogs.AlertAsync("Couldn't open this entry", "Please try again.");
        }
        finally
        {
            if (ReferenceEquals(cts, _loadCts)) IsBusy = false;
        }
    }

    private void Apply(HistoryEntry entry)
    {
        Entry = entry;
        Payload = entry.Payload;
        IsFavorite = entry.IsFavorite;
        _savedNotes = entry.Notes?.Trim() ?? string.Empty;
        _savedTags = entry.Tags?.Trim() ?? string.Empty;
        Notes = _savedNotes;
        Tags = _savedTags;

        QrContent content;
        try
        {
            content = _parser.Parse(entry.Payload);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Payload could not be parsed");
            content = new TextContent(entry.Payload);
        }

        Content = content;
        var summary = _summarizer.Summarize(content);
        Heading = string.IsNullOrWhiteSpace(entry.Title) ? summary.Title : entry.Title;
        Subtitle = entry.Subtitle ?? summary.Subtitle;
        Glyph = string.IsNullOrEmpty(summary.IconGlyph) ? Glyphs.For(content.Type) : summary.IconGlyph;
        TypeName = ContentTypeNames.For(content.Type);
        Title = TypeName;

        var actions = _actionProvider.GetActions(content)
            .Select(a => new ActionItem(a, new AsyncRelayCommand(() => ExecuteAsync(a))))
            .ToList();
        PrimaryAction = actions.FirstOrDefault(a => a.IsPrimary) ?? actions.FirstOrDefault();
        SecondaryActions = actions.Where(a => !ReferenceEquals(a, PrimaryAction)).ToList();
    }

    private async Task RenderAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        IsImageLoading = true;
        ImageError = null;
        try
        {
            var style = entry.Kind == HistoryKind.Generated ? CoreJson.DeserializeStyle(entry.StyleJson) : null;
            var format = entry.Format.IsWritable() ? entry.Format : BarcodeFormat.QrCode;
            var options = new RenderOptions(SizePixels: PreviewSize);
            var ecl = _settings.Current.DefaultErrorCorrection;

            // TextContent re-encodes the payload byte-for-byte, so the code is identical to the original.
            var request = new GenerationRequest(new TextContent(entry.Payload), format, ecl, style, options);
            var result = await _generation.GenerateAsync(request, GenerationMode.Preview, cancellationToken);
            if (result.IsFailure && format != BarcodeFormat.QrCode)
                result = await _generation.GenerateAsync(request with { Format = BarcodeFormat.QrCode }, GenerationMode.Preview, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;
            if (result.IsFailure)
            {
                ImageError = result.Error.Message;
                CodeImage = null;
                _imageBytes = null;
                return;
            }

            var bytes = result.Value.Image.Data;
            _imageBytes = bytes;
            CodeImage = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Rendering history entry failed");
            ImageError = "The code couldn't be drawn.";
        }
        finally
        {
            IsImageLoading = false;
        }
    }

    private async Task ExecuteAsync(ContentAction action)
    {
        if (Content is null) return;
        try
        {
            await _executor.ExecuteAsync(action, Content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Action {Action} failed", action.Kind);
            await Dialogs.AlertAsync("Something went wrong", ex.Message);
        }
    }

    // ---------- Commands ----------

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Entry is null) return;
        var value = !IsFavorite;
        IsFavorite = value;
        _haptics.Click();
        try
        {
            await _history.SetFavoriteAsync(Entry.Id, value);
            Entry = Entry with { IsFavorite = value };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Updating favourite failed");
            IsFavorite = !value;
            await Dialogs.AlertAsync("Couldn't update favourite", "Please try again.");
        }
    }

    [RelayCommand]
    private Task SaveNotesAsync() => RunBusyAsync(async () =>
    {
        if (Entry is null) return;
        var notes = Notes.Trim();
        var tags = NormalizeTags(Tags);
        await _history.UpdateNotesAsync(Entry.Id, notes.Length == 0 ? null : notes, tags.Length == 0 ? null : tags);
        _savedNotes = notes;
        _savedTags = tags;
        Tags = tags;
        Notes = notes;
        OnPropertyChanged(nameof(HasUnsavedNotes));
        await Dialogs.ToastAsync("Notes saved");
    }, "Couldn't save notes");

    [RelayCommand]
    private async Task CopyPayloadAsync()
    {
        if (string.IsNullOrEmpty(Payload)) return;
        await _clipboard.SetTextAsync(Payload);
        _haptics.Click();
        await Dialogs.ToastAsync("Copied to clipboard");
    }

    [RelayCommand]
    private Task ShareImageAsync() => RunBusyAsync(async () =>
    {
        if (_imageBytes is null) return;
        var path = await _files.WriteTempFileAsync(_imageBytes, ImageFileName());
        await _share.ShareFileAsync(Heading, path, "image/png");
    }, "Couldn't share the image");

    [RelayCommand]
    private Task SaveImageAsync() => RunBusyAsync(async () =>
    {
        if (_imageBytes is null) return;
        var result = await _files.SaveImageToGalleryAsync(_imageBytes, ImageFileName(), "image/png");
        if (result.IsSuccess)
        {
            _haptics.Success();
            await Dialogs.ToastAsync("Saved to your photos");
        }
        else
        {
            await Dialogs.AlertAsync("Couldn't save the image", result.Error.Message);
        }
    }, "Couldn't save the image");

    [RelayCommand]
    private Task EditAndRecreateAsync()
    {
        if (Entry is null) return Task.CompletedTask;
        return _navigation.GoToTabAsync(Routes.Create, new Dictionary<string, object> { [Routes.PayloadParameter] = Entry.Payload });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Entry is null) return;
        if (!await Dialogs.ConfirmAsync("Delete this code?", "It will be removed from your history. This can't be undone.", "Delete", "Cancel"))
            return;

        await RunBusyAsync(async () =>
        {
            await _history.DeleteAsync(Entry.Id);
            await Dialogs.ToastAsync("Deleted");
            await _navigation.GoBackAsync();
        }, "Couldn't delete");
    }

    private string ImageFileName()
    {
        var slug = new string(Heading.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim().Replace(' ', '-');
        if (slug.Length == 0) slug = "code";
        if (slug.Length > 40) slug = slug[..40];
        return $"quickqr-{slug}.png";
    }

    private static string NormalizeTags(string tags) =>
        string.Join(", ", tags
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
