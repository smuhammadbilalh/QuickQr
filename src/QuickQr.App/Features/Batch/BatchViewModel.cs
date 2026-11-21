using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Controls;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Core.Settings;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.Batch;

/// <summary>One parsed input row shown in the preview list.</summary>
public sealed class BatchRowItem(int number, BatchItem item)
{
    public string Number { get; } = number.ToString(CultureInfo.CurrentCulture);
    public string Payload { get; } = item.Payload.Length > 120 ? item.Payload[..120] + "…" : item.Payload;
    public string FileName { get; } = string.IsNullOrWhiteSpace(item.FileName) ? "(automatic name)" : item.FileName;
    public string SemanticDescription => $"Row {Number}: {Payload}, file {FileName}";
}

/// <summary>
/// Business: many codes from pasted text or a CSV/TXT file, written to a ZIP (with manifest) or laid out on
/// printable label sheets (PDF). Output goes to the app cache, then the user shares or saves it.
/// </summary>
public sealed partial class BatchViewModel : ViewModelBase
{
    public const int PreviewRowLimit = 50;
    private const long MaxInputBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IBatchInputParser _parser;
    private readonly IBatchGenerator _generator;
    private readonly IPrintSheetGenerator _printSheets;
    private readonly IBrandKitStore _brandKits;
    private readonly IPremiumGate _gate;
    private readonly IEntitlementService _entitlements;
    private readonly IMediaPickerService _media;
    private readonly IFileExportService _files;
    private readonly IShareService _share;
    private readonly IClipboardService _clipboard;
    private readonly IFileSystem _fileSystem;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private CancellationTokenSource? _parseCts;
    private CancellationTokenSource? _generationCts;
    private BatchParseResult _parsed = new([], []);

    public BatchViewModel(
        IBatchInputParser parser,
        IBatchGenerator generator,
        IPrintSheetGenerator printSheets,
        IBrandKitStore brandKits,
        IPremiumGate gate,
        IEntitlementService entitlements,
        IMediaPickerService media,
        IFileExportService files,
        IShareService share,
        IClipboardService clipboard,
        IAppSettingsService settings,
        IFileSystem fileSystem,
        IDialogService dialogs,
        ILogger<BatchViewModel> logger) : base(dialogs, logger)
    {
        _parser = parser;
        _generator = generator;
        _printSheets = printSheets;
        _brandKits = brandKits;
        _gate = gate;
        _entitlements = entitlements;
        _media = media;
        _files = files;
        _share = share;
        _clipboard = clipboard;
        _fileSystem = fileSystem;
        Title = "Bulk create";

        Formats = new OptionGroup<BarcodeFormat>();
        foreach (var format in BarcodeFormatExtensions.Writable)
            Formats.Add(format, format.DisplayName());
        Formats.SelectionChanged += (_, _) => OnPropertyChanged(nameof(IsQrFormat));

        ErrorCorrections = new OptionGroup<ErrorCorrectionLevel>()
            .Add(ErrorCorrectionLevel.Low, "Low", caption: "7 %")
            .Add(ErrorCorrectionLevel.Medium, "Medium", caption: "15 %")
            .Add(ErrorCorrectionLevel.Quartile, "Quartile", caption: "25 %")
            .Add(ErrorCorrectionLevel.High, "High", caption: "30 %");

        Sizes = new OptionGroup<int>()
            .Add(512, "512 px")
            .Add(1024, "1024 px")
            .Add(2048, "2048 px");

        ExportFormats = new OptionGroup<ExportFormat>()
            .Add(ExportFormat.Png, "PNG")
            .Add(ExportFormat.Jpeg, "JPEG")
            .Add(ExportFormat.Webp, "WebP")
            .Add(ExportFormat.Svg, "SVG")
            .Add(ExportFormat.Pdf, "PDF");

        Papers = new OptionGroup<PaperSize>()
            .Add(PaperSize.A4, "A4")
            .Add(PaperSize.Letter, "US Letter");

        Styles = new OptionGroup<QrStyle>();
        BuildStyles([]);

        try
        {
            var defaults = settings.Current;
            if (defaults.DefaultFormat.IsWritable()) Formats.Value = defaults.DefaultFormat;
            ErrorCorrections.Value = defaults.DefaultErrorCorrection;
            Sizes.Value = defaults.DefaultExportSize >= 2048 ? 2048 : defaults.DefaultExportSize >= 1024 ? 1024 : 512;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not read generation defaults");
        }

        RefreshLocks();
    }

    public OptionGroup<BarcodeFormat> Formats { get; }
    public OptionGroup<ErrorCorrectionLevel> ErrorCorrections { get; }
    public OptionGroup<QrStyle> Styles { get; }
    public OptionGroup<int> Sizes { get; }
    public OptionGroup<ExportFormat> ExportFormats { get; }
    public OptionGroup<PaperSize> Papers { get; }
    public ObservableCollection<BatchRowItem> PreviewRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    public bool IsQrFormat => Formats.Value == BarcodeFormat.QrCode;

    // ---------- Access ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    public partial bool IsUnlocked { get; private set; }

    public bool IsLocked => !IsUnlocked;

    [ObservableProperty]
    public partial bool ArePrintSheetsLocked { get; private set; }

    // ---------- Input ----------
    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasHeaderRow { get; set; }

    [ObservableProperty]
    public partial bool UseFileNameColumn { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems), nameof(HasNoItems), nameof(ItemCountText), nameof(PreviewFooter), nameof(HasMoreRows))]
    public partial int ItemCount { get; private set; }

    [ObservableProperty]
    public partial bool HasWarnings { get; private set; }

    public bool HasItems => ItemCount > 0;
    public bool HasNoItems => ItemCount == 0;
    public string ItemCountText => ItemCount == 1 ? "1 code" : string.Create(CultureInfo.CurrentCulture, $"{ItemCount:N0} codes");
    public bool HasMoreRows => ItemCount > PreviewRowLimit;
    public string PreviewFooter => string.Create(CultureInfo.CurrentCulture, $"Showing the first {PreviewRowLimit} of {ItemCount:N0} rows.");

    // ---------- Print sheet ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColumnsText), nameof(SheetLayoutText))]
    public partial double Columns { get; set; } = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowsText), nameof(SheetLayoutText))]
    public partial double Rows { get; set; } = 6;

    [ObservableProperty]
    public partial bool PrintLabels { get; set; } = true;

    public string ColumnsText => $"{(int)Columns} columns";
    public string RowsText => $"{(int)Rows} rows";

    public string SheetLayoutText
    {
        get
        {
            var perSheet = (int)Columns * (int)Rows;
            var sheets = perSheet == 0 ? 0 : (int)Math.Ceiling(ItemCount / (double)perSheet);
            return string.Create(CultureInfo.CurrentCulture, $"{perSheet} labels per sheet · {sheets:N0} {(sheets == 1 ? "sheet" : "sheets")}");
        }
    }

    // ---------- Progress and output ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotGenerating))]
    public partial bool IsGenerating { get; private set; }

    public bool IsNotGenerating => !IsGenerating;

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    public partial string? OutputPath { get; private set; }

    [ObservableProperty]
    public partial string ResultText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailures))]
    public partial string? FailureText { get; private set; }

    public bool HasOutput => OutputPath is not null;
    public bool HasFailures => FailureText is not null;

    private string _outputMime = "application/zip";

    public override async Task OnAppearingAsync()
    {
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        _entitlements.EntitlementsChanged += OnEntitlementsChanged;
        RefreshLocks();

        try
        {
            BuildStyles(await _brandKits.GetAllAsync());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading brand kits failed");
        }
    }

    public override Task OnDisappearingAsync()
    {
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        return Task.CompletedTask;
    }

    partial void OnInputTextChanged(string value) => ScheduleParse();

    partial void OnHasHeaderRowChanged(bool value) => ScheduleParse();

    partial void OnUseFileNameColumnChanged(bool value) => ScheduleParse();

    // ---------- Commands ----------
    [RelayCommand]
    private async Task UpgradeAsync()
    {
        await _gate.EnsureAsync(PremiumFeature.BatchGeneration);
        RefreshLocks();
    }

    [RelayCommand]
    private Task ImportFileAsync() => RunBusyAsync(async () =>
    {
        var file = await _media.PickFileAsync(new[] { ".csv", ".txt", ".tsv" });
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        if (stream.CanSeek && stream.Length > MaxInputBytes)
        {
            await Dialogs.AlertAsync("File too large", "Choose a file smaller than 10 MB.");
            return;
        }

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        HasHeaderRow = file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                       file.FileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase);
        InputText = text;
    }, "Couldn't import the file");

    [RelayCommand]
    private Task PasteAsync() => RunBusyAsync(async () =>
    {
        var text = await _clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            await Dialogs.ToastAsync("The clipboard is empty");
            return;
        }

        InputText = string.IsNullOrEmpty(InputText) ? text : InputText.TrimEnd() + Environment.NewLine + text;
    }, "Couldn't paste");

    [RelayCommand]
    private void ClearInput()
    {
        InputText = string.Empty;
        ClearOutput();
    }

    [RelayCommand]
    private void CancelGeneration() => _generationCts?.Cancel();

    [RelayCommand]
    private async Task GenerateZipAsync()
    {
        var items = await PrepareAsync(PremiumFeature.BatchGeneration, includeExportOptions: true);
        if (items is null) return;

        var request = new BatchRequest(items, Formats.Value, ErrorCorrections.Value, Styles.Value,
            new RenderOptions(Sizes.Value, ExportFormats.Value));
        var path = NewOutputPath(".zip");

        await RunGenerationAsync(path, "application/zip", async (stream, progress, ct) =>
        {
            var result = await _generator.GenerateZipAsync(request, stream, progress, ct);
            if (result.IsFailure) return (false, result.Error.Message, null);

            var summary = result.Value;
            var failures = summary.Failures.Count == 0
                ? null
                : string.Join(Environment.NewLine, summary.Failures.Take(10).Select(f => $"Row {f.Index + 1} ({f.FileName}): {f.Reason}")) +
                  (summary.Failures.Count > 10 ? string.Create(CultureInfo.CurrentCulture, $"{Environment.NewLine}…and {summary.Failures.Count - 10:N0} more (see manifest.csv).") : string.Empty);
            var text = summary.Failures.Count == 0
                ? string.Create(CultureInfo.CurrentCulture, $"{summary.Succeeded:N0} codes are ready in a ZIP file.")
                : string.Create(CultureInfo.CurrentCulture, $"{summary.Succeeded:N0} codes created, {summary.Failures.Count:N0} skipped.");
            return (true, text, failures);
        });
    }

    [RelayCommand]
    private async Task GeneratePrintSheetAsync()
    {
        var items = await PrepareAsync(PremiumFeature.PrintSheets, includeExportOptions: false);
        if (items is null) return;

        var request = new PrintSheetRequest(items, Formats.Value, ErrorCorrections.Value, Styles.Value,
            Papers.Value, (int)Columns, (int)Rows, PrintLabels);
        var path = NewOutputPath(".pdf");

        await RunGenerationAsync(path, "application/pdf", async (stream, _, ct) =>
        {
            var result = await _printSheets.GeneratePdfAsync(request, stream, ct);
            return result.IsSuccess
                ? (true, string.Create(CultureInfo.CurrentCulture, $"Print sheet with {items.Count:N0} codes is ready."), null)
                : (false, result.Error.Message, null);
        });
    }

    [RelayCommand]
    private Task ShareOutputAsync() => RunBusyAsync(async () =>
    {
        if (OutputPath is not { } path || !File.Exists(path)) return;
        await _share.ShareFileAsync("QuickQr codes", path, _outputMime);
    }, "Couldn't share");

    [RelayCommand]
    private Task SaveOutputAsync() => RunBusyAsync(async () =>
    {
        if (OutputPath is not { } path || !File.Exists(path)) return;
        await using var stream = File.OpenRead(path);
        var result = await _files.SaveFileAsync(stream, Path.GetFileName(path));
        if (result.IsSuccess) await Dialogs.ToastAsync("File saved");
        else if (result.Error.Code != ErrorCodes.Cancelled) await Dialogs.AlertAsync("Couldn't save", result.Error.Message);
    }, "Couldn't save");

    // ---------- Generation helpers ----------
    /// <summary>Checks entitlements (feature, style, format) and returns the full item list, or null to abort.</summary>
    private async Task<IReadOnlyList<BatchItem>?> PrepareAsync(PremiumFeature feature, bool includeExportOptions)
    {
        if (!await _gate.EnsureAsync(feature))
        {
            RefreshLocks();
            return null;
        }

        _parseCts?.Cancel();
        Parse();
        if (_parsed.Items.Count == 0)
        {
            await Dialogs.AlertAsync("Nothing to create", "Paste some lines or import a CSV file first.");
            return null;
        }

        var options = includeExportOptions ? new RenderOptions(Sizes.Value, ExportFormats.Value) : null;
        var needed = FeatureCatalog.FeaturesUsedBy(Styles.Value, options).ToList();
        if (Formats.Value != BarcodeFormat.QrCode) needed.Add(PremiumFeature.AdvancedFormats);
        foreach (var required in needed.Distinct())
        {
            if (!await _gate.EnsureAsync(required)) return null;
        }

        return _parsed.Items;
    }

    private async Task RunGenerationAsync(
        string path,
        string mimeType,
        Func<Stream, IProgress<BatchProgress>, CancellationToken, Task<(bool Success, string Message, string? Failures)>> generate)
    {
        using var cts = new CancellationTokenSource();
        _generationCts = cts;
        var cancellationToken = cts.Token;

        ClearOutput();
        IsGenerating = true;
        Progress = 0;
        ProgressText = "Preparing…";
        var progress = new Progress<BatchProgress>(p =>
        {
            Progress = p.Total == 0 ? 0 : p.Completed / (double)p.Total;
            ProgressText = string.Create(CultureInfo.CurrentCulture, $"{p.Completed:N0} of {p.Total:N0}");
        });

        try
        {
            (bool Success, string Message, string? Failures) outcome;
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                outcome = await Task.Run(() => generate(stream, progress, cancellationToken), cancellationToken);
            }

            if (!outcome.Success)
            {
                TryDelete(path);
                await Dialogs.AlertAsync("Couldn't create the codes", outcome.Message);
                return;
            }

            _outputMime = mimeType;
            OutputPath = path;
            ResultText = outcome.Message;
            FailureText = outcome.Failures;
            Progress = 1;
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            await Dialogs.ToastAsync("Cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(path);
            Logger.LogError(ex, "Batch generation failed");
            await Dialogs.AlertAsync("Couldn't create the codes", ex.Message);
        }
        finally
        {
            _generationCts = null;
            IsGenerating = false;
            ProgressText = string.Empty;
        }
    }

    private string NewOutputPath(string extension)
    {
        var directory = Path.Combine(_fileSystem.CacheDirectory, "batch");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"QuickQr-codes-{DateTime.Now:yyyyMMdd-HHmmss}{extension}"));
    }

    private void ClearOutput()
    {
        OutputPath = null;
        ResultText = string.Empty;
        FailureText = null;
        Progress = 0;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            Logger.LogDebug(ex, "Could not delete partial output {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogDebug(ex, "Could not delete partial output {Path}", path);
        }
    }

    // ---------- Parsing ----------
    private void ScheduleParse()
    {
        _parseCts?.Cancel();
        var cts = new CancellationTokenSource();
        _parseCts = cts;
        _ = ParseAfterDelayAsync(cts.Token);
    }

    private async Task ParseAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ParseDebounce, cancellationToken);
            Parse();
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input.
        }
    }

    private void Parse()
    {
        try
        {
            _parsed = string.IsNullOrWhiteSpace(InputText)
                ? new BatchParseResult([], [])
                : _parser.Parse(InputText, new BatchParseOptions(HasHeaderRow, FileNameColumn: UseFileNameColumn ? 1 : null));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Parsing batch input failed");
            _parsed = new BatchParseResult([], new[] { "The input could not be read. Check the CSV format." });
        }

        PreviewRows.Clear();
        for (var i = 0; i < Math.Min(PreviewRowLimit, _parsed.Items.Count); i++)
            PreviewRows.Add(new BatchRowItem(i + 1, _parsed.Items[i]));

        Warnings.Clear();
        foreach (var warning in _parsed.Warnings.Take(20))
            Warnings.Add(warning);
        HasWarnings = Warnings.Count > 0;

        ItemCount = _parsed.Items.Count;
        OnPropertyChanged(nameof(SheetLayoutText));
    }

    // ---------- Styles and entitlements ----------
    private void BuildStyles(IReadOnlyList<BrandKit> kits)
    {
        var selected = Styles.SelectedItem?.Label;
        Styles.Clear();
        foreach (var (name, style) in StylePresets.All)
            Styles.Add(style, name);
        foreach (var kit in kits.OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
            Styles.Add(kit.Style, kit.Name, "\ue41d", "Brand kit");

        var previous = Styles.Items.FirstOrDefault(i => i.Label == selected);
        if (previous is not null) Styles.Select(previous);
    }

    private void RefreshLocks()
    {
        IsUnlocked = _gate.IsUnlocked(PremiumFeature.BatchGeneration);
        ArePrintSheetsLocked = !_gate.IsUnlocked(PremiumFeature.PrintSheets);
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => RunOnUi(RefreshLocks);

    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
}
