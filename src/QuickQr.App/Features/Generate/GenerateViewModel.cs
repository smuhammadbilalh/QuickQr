using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Controls;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Generation;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.Generate;

/// <summary>
/// Create tab: content-type picker, per-type form, debounced live preview (premium designs are previewable),
/// options, design studio and export actions (entitlement-gated, followed by a decode check).
/// </summary>
public sealed partial class GenerateViewModel : ViewModelBase, IQueryAttributable
{
    public const int PreviewSize = 512;
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IQrGenerationService _generation;
    private readonly IContentEncoder _encoder;
    private readonly IContentParser _parser;
    private readonly ISecurePayloadProtector _protector;
    private readonly IBarcodeImageDecoder _decoder;
    private readonly IEntitlementService _entitlements;
    private readonly IPremiumGate _gate;
    private readonly IFileExportService _files;
    private readonly IShareService _share;
    private readonly IClipboardService _clipboard;
    private readonly INavigationService _navigation;
    private readonly IHapticsService _haptics;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly Dictionary<ContentKind, ContentFormViewModel> _forms = [];

    private CancellationTokenSource? _previewCts;
    private IReadOnlyList<PremiumFeature> _lockedFeatures = [];
    private bool _syncingSelection;
    private bool _hasAppeared;

    public GenerateViewModel(
        IQrGenerationService generation,
        IContentEncoder encoder,
        IContentParser parser,
        ISecurePayloadProtector protector,
        IBarcodeImageDecoder decoder,
        IEntitlementService entitlements,
        IPremiumGate gate,
        IAppSettingsService settings,
        IFileExportService files,
        IShareService share,
        IClipboardService clipboard,
        INavigationService navigation,
        IHapticsService haptics,
        DesignStudioViewModel design,
        IDialogService dialogs,
        ILogger<GenerateViewModel> logger) : base(dialogs, logger)
    {
        _generation = generation;
        _encoder = encoder;
        _parser = parser;
        _protector = protector;
        _decoder = decoder;
        _entitlements = entitlements;
        _gate = gate;
        _files = files;
        _share = share;
        _clipboard = clipboard;
        _navigation = navigation;
        _haptics = haptics;
        Design = design;
        Title = "Create";

        ContentTypes = new OptionGroup<ContentKind>();
        foreach (var info in ContentKinds.All)
            ContentTypes.Add(info.Kind, info.Name, info.Glyph);

        Formats = new OptionGroup<BarcodeFormat>();
        foreach (var format in BarcodeFormatExtensions.Writable)
            Formats.Add(format, format.DisplayName());

        ErrorCorrections = new OptionGroup<ErrorCorrectionLevel>()
            .Add(ErrorCorrectionLevel.Low, "Low", caption: "7 %")
            .Add(ErrorCorrectionLevel.Medium, "Medium", caption: "15 %")
            .Add(ErrorCorrectionLevel.Quartile, "Quartile", caption: "25 %")
            .Add(ErrorCorrectionLevel.High, "High", caption: "30 %");

        Sizes = new OptionGroup<int>()
            .Add(512, "512 px")
            .Add(1024, "1024 px")
            .Add(2048, "2048 px")
            .Add(4096, "4096 px");

        ExportFormats = new OptionGroup<ExportFormat>()
            .Add(ExportFormat.Png, "PNG")
            .Add(ExportFormat.Jpeg, "JPEG")
            .Add(ExportFormat.Webp, "WebP")
            .Add(ExportFormat.Svg, "SVG", caption: "Vector")
            .Add(ExportFormat.Pdf, "PDF", caption: "Print");

        ApplyDefaults(settings);

        CurrentForm = GetOrCreateForm(ContentTypes.Value);
        CurrentForm.PropertyChanged += OnFormPropertyChanged;
        ActiveForms.Add(CurrentForm);
        UpdateFormatState();

        ContentTypes.SelectionChanged += (_, _) => OnContentTypeChanged();
        Formats.SelectionChanged += (_, _) => OnFormatChanged();
        ErrorCorrections.SelectionChanged += (_, _) => SchedulePreview();
        Sizes.SelectionChanged += (_, _) => UpdateLockedFeatures();
        ExportFormats.SelectionChanged += (_, _) => UpdateLockedFeatures();
        Design.StyleChanged += (_, _) => SchedulePreview();
        Design.PropertyChanged += OnDesignPropertyChanged;

        RefreshLocks();
    }

    public DesignStudioViewModel Design { get; }
    public OptionGroup<ContentKind> ContentTypes { get; }
    public OptionGroup<BarcodeFormat> Formats { get; }
    public OptionGroup<ErrorCorrectionLevel> ErrorCorrections { get; }
    public OptionGroup<int> Sizes { get; }
    public OptionGroup<ExportFormat> ExportFormats { get; }

    /// <summary>Exactly one item: the active form (rendered through <see cref="ContentFormTemplateSelector"/>).</summary>
    public ObservableCollection<ContentFormViewModel> ActiveForms { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormGlyph))]
    public partial ContentFormViewModel CurrentForm { get; private set; }

    public string FormTitle => CurrentForm.Info.Name;
    public string FormGlyph => CurrentForm.Info.Glyph;

    [ObservableProperty]
    public partial bool IsFormLocked { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string? ValidationMessage { get; private set; }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(ShowPlaceholder))]
    public partial byte[]? PreviewImage { get; private set; }

    public bool HasPreview => PreviewImage is not null;
    public bool ShowPlaceholder => PreviewImage is null;

    [ObservableProperty]
    public partial bool IsPreviewBusy { get; private set; }

    [ObservableProperty]
    public partial string PlaceholderText { get; private set; } = "Fill in the form to see your code";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewDescription))]
    public partial string? Payload { get; private set; }

    public string PreviewDescription => Payload is null ? "No code yet" : $"{Formats.Value.DisplayName()} preview";

    // ---------- Capacity ----------
    [ObservableProperty]
    public partial bool ShowCapacity { get; private set; }

    [ObservableProperty]
    public partial double CapacityProgress { get; private set; }

    [ObservableProperty]
    public partial string CapacityText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCapacityWarning { get; private set; }

    [ObservableProperty]
    public partial bool IsCapacityExceeded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapacityHint))]
    public partial string? CapacityHint { get; private set; }

    public bool HasCapacityHint => CapacityHint is not null;

    // ---------- Premium ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedFeatures))]
    public partial string? LockedFeaturesText { get; private set; }

    public bool HasLockedFeatures => LockedFeaturesText is not null;

    // ---------- Options state ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogoForcingHigh))]
    public partial bool IsQrFormat { get; private set; } = true;

    public bool IsLogoForcingHigh => IsQrFormat && Design.HasLogo;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(Routes.PayloadParameter, out var value) || value is not string payload || payload.Length == 0)
            return;

        // Navigation passes single-use (read-only) parameters, so they are not re-applied on later visits.
        _ = PrefillSafelyAsync(payload);
    }

    private async Task PrefillSafelyAsync(string payload)
    {
        try
        {
            await PrefillAsync(payload);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Prefilling the form failed");
        }
    }

    public override async Task OnAppearingAsync()
    {
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        _entitlements.EntitlementsChanged += OnEntitlementsChanged;
        RefreshLocks();
        UpdateLockedFeatures();
        await Design.LoadBrandKitsAsync();

        if (!_hasAppeared)
        {
            _hasAppeared = true;
            SchedulePreview();
        }
    }

    public override Task OnDisappearingAsync()
    {
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        return Task.CompletedTask;
    }

    // ---------- Actions ----------
    [RelayCommand]
    private Task SaveToGalleryAsync() => RunBusyAsync(async () =>
    {
        var code = await ExportAsync();
        if (code is null) return;

        if (code.Image.Format is ExportFormat.Svg or ExportFormat.Pdf)
        {
            // Photo libraries only take raster images: offer a file instead.
            await SaveFileCoreAsync(code);
            return;
        }

        var result = await _files.SaveImageToGalleryAsync(code.Image.Data, FileName(code.Image), code.Image.MimeType);
        if (await ReportAsync(result, "Saved to your gallery")) _haptics.Success();
    }, "Couldn't save the code");

    [RelayCommand]
    private Task ShareAsync() => RunBusyAsync(async () =>
    {
        var code = await ExportAsync();
        if (code is null) return;

        var path = await _files.WriteTempFileAsync(code.Image.Data, FileName(code.Image));
        await _share.ShareFileAsync("Share code", path, code.Image.MimeType);
    }, "Couldn't share the code");

    [RelayCommand]
    private Task SaveAsFileAsync() => RunBusyAsync(async () =>
    {
        var code = await ExportAsync();
        if (code is not null) await SaveFileCoreAsync(code);
    }, "Couldn't save the file");

    [RelayCommand]
    private Task CopyTextAsync() => RunBusyAsync(async () =>
    {
        var form = CurrentForm;
        if (await CheckFormAsync(form) is false) return;

        var content = await form.BuildContentAsync(CancellationToken.None);
        if (content is SecureMessageContent && !await _gate.EnsureAsync(PremiumFeature.SecureQr)) return;

        var encoded = _encoder.Encode(content);
        if (encoded.IsFailure)
        {
            await Dialogs.AlertAsync("Check the content", encoded.Error.Message);
            return;
        }

        await _clipboard.SetTextAsync(encoded.Value);
        _haptics.Click();
        await Dialogs.ToastAsync("Content copied");
    }, "Couldn't copy");

    [RelayCommand]
    private Task BulkCreateAsync() => _navigation.GoToAsync(Routes.Batch);

    [RelayCommand]
    private Task UpgradeAsync() => _gate.ShowPaywallAsync(_lockedFeatures.Count > 0 ? _lockedFeatures[0] : null);

    [RelayCommand]
    private void ClearForm()
    {
        var kind = CurrentForm.Kind;
        CurrentForm.PropertyChanged -= OnFormPropertyChanged;
        _forms.Remove(kind);
        var fresh = GetOrCreateForm(kind);
        ShowForm(fresh);
    }

    // ---------- Export pipeline ----------
    /// <summary>Validates, asks for any locked feature (abort when still locked), renders in Export mode and runs the decode check.</summary>
    private async Task<GeneratedCode?> ExportAsync()
    {
        var form = CurrentForm;
        if (await CheckFormAsync(form) is false) return null;

        var content = await form.BuildContentAsync(CancellationToken.None);
        var request = BuildRequest(content, export: true);

        foreach (var feature in _generation.GetLockedFeatures(request).Distinct())
        {
            if (!await _gate.EnsureAsync(feature)) return null;
        }

        // Entitlements may have changed while the paywall was open: rebuild the request.
        request = BuildRequest(content, export: true);
        var result = await Task.Run(() => _generation.GenerateAsync(request, GenerationMode.Export));
        if (result.IsFailure)
        {
            await Dialogs.AlertAsync("Couldn't create the code", result.Error.Message);
            return null;
        }

        return await IsScannableAsync(result.Value) ||
               await Dialogs.ConfirmAsync("Check your design",
                   "This design may be hard to scan — increase contrast or reduce the logo.",
                   "Continue anyway", "Edit design")
            ? result.Value
            : null;
    }

    private async Task<bool> CheckFormAsync(ContentFormViewModel form)
    {
        if (form.IsEmpty)
        {
            await Dialogs.AlertAsync("Nothing to create yet", "Fill in the form first.");
            return false;
        }

        if (form.Validate() is { } message)
        {
            await Dialogs.AlertAsync("Check the content", message);
            return false;
        }

        return true;
    }

    /// <summary>Decodes raster exports back and compares with the payload. Vector/PDF output and decoder failures count as scannable.</summary>
    private async Task<bool> IsScannableAsync(GeneratedCode code)
    {
        if (code.Image.Format is ExportFormat.Svg or ExportFormat.Pdf) return true;

        try
        {
            var data = code.Image.Data;
            var decoded = await Task.Run(async () =>
            {
                using var stream = new MemoryStream(data, writable: false);
                return await _decoder.DecodeAsync(stream, new DecodeOptions(TryHarder: true, TryInverted: true, DetectMultiple: false));
            });
            return decoded.Any(d => string.Equals(d.Text, code.Payload, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Decode check failed");
            return true;
        }
    }

    private async Task SaveFileCoreAsync(GeneratedCode code)
    {
        using var stream = new MemoryStream(code.Image.Data, writable: false);
        var result = await _files.SaveFileAsync(stream, FileName(code.Image));
        if (await ReportAsync(result, "File saved")) _haptics.Success();
    }

    private async Task<bool> ReportAsync(Result<string> result, string successMessage)
    {
        if (result.IsSuccess)
        {
            await Dialogs.ToastAsync(successMessage);
            return true;
        }

        if (result.Error.Code != ErrorCodes.Cancelled)
            await Dialogs.AlertAsync("Couldn't save", result.Error.Message);
        return false;
    }

    private string FileName(RenderedImage image) =>
        string.Create(CultureInfo.InvariantCulture, $"QuickQr-{CurrentForm.Info.Name.Replace(' ', '-')}-{DateTime.Now:yyyyMMdd-HHmmss}{image.FileExtension}");

    private GenerationRequest BuildRequest(QrContent content, bool export)
    {
        var options = export
            ? new RenderOptions(Sizes.Value, ExportFormats.Value)
            : new RenderOptions(PreviewSize, ExportFormat.Png);
        return new GenerationRequest(content, Formats.Value, ErrorCorrections.Value, Design.BuildStyle(), options);
    }

    // ---------- Live preview ----------
    private void SchedulePreview()
    {
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        UpdateLockedFeatures();
        _ = UpdatePreviewAsync(cts.Token);
    }

    private async Task UpdatePreviewAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PreviewDebounce, cancellationToken);
            var form = CurrentForm;

            if (form.IsEmpty)
            {
                ClearPreview("Fill in the form to see your code", validation: null);
                return;
            }

            if (form.Validate() is { } invalid)
            {
                ClearPreview("Fix the highlighted problem to see your code", invalid);
                return;
            }

            IsPreviewBusy = true;
            var content = await form.BuildContentAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var encoded = _encoder.Encode(content);
            if (encoded.IsFailure)
            {
                ClearPreview("Fix the highlighted problem to see your code", encoded.Error.Message);
                return;
            }

            UpdateCapacity(encoded.Value);
            var request = BuildRequest(content, export: false);
            var result = await Task.Run(() => _generation.GenerateAsync(request, GenerationMode.Preview, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                Payload = encoded.Value;
                PreviewImage = null;
                PlaceholderText = "This content can't be shown in the selected format";
                ValidationMessage = result.Error.Message;
                return;
            }

            Payload = result.Value.Payload;
            PreviewImage = result.Value.Image.Data;
            ValidationMessage = null;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Preview failed");
            if (!cancellationToken.IsCancellationRequested)
                ClearPreview("The preview isn't available right now", "Something went wrong while drawing the preview.");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsPreviewBusy = false;
        }
    }

    private void ClearPreview(string placeholder, string? validation)
    {
        PreviewImage = null;
        Payload = null;
        PlaceholderText = placeholder;
        ValidationMessage = validation;
        ShowCapacity = false;
    }

    /// <summary>UTF-8 payload size against QR byte-mode capacity (version 40) at the effective error correction.</summary>
    private void UpdateCapacity(string payload)
    {
        if (!IsQrFormat)
        {
            ShowCapacity = false;
            return;
        }

        var ecl = Design.HasLogo ? ErrorCorrectionLevel.High : ErrorCorrections.Value;
        var max = QrByteCapacity(ecl);
        var used = Encoding.UTF8.GetByteCount(payload);

        CapacityProgress = Math.Min(1d, used / (double)max);
        IsCapacityExceeded = used > max;
        IsCapacityWarning = !IsCapacityExceeded && used > max * 0.8;
        CapacityText = string.Create(CultureInfo.CurrentCulture, $"{used:N0} / {max:N0} bytes");
        CapacityHint = IsCapacityExceeded
            ? "Too much data for one QR code at this error correction. Shorten the content or lower the error correction."
            : IsCapacityWarning
                ? "Very dense codes are harder to scan. Consider shortening the content."
                : null;
        ShowCapacity = true;
    }

    public static int QrByteCapacity(ErrorCorrectionLevel level) => level switch
    {
        ErrorCorrectionLevel.Low => 2953,
        ErrorCorrectionLevel.Medium => 2331,
        ErrorCorrectionLevel.Quartile => 1663,
        _ => 1273
    };

    private void UpdateLockedFeatures()
    {
        try
        {
            QrContent probe = CurrentForm.Kind == ContentKind.Secure
                ? new SecureMessageContent(string.Empty)
                : new TextContent(string.Empty);
            _lockedFeatures = _generation.GetLockedFeatures(BuildRequest(probe, export: true)).Distinct().ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not evaluate premium features");
            _lockedFeatures = [];
        }

        LockedFeaturesText = _lockedFeatures.Count == 0
            ? null
            : "Uses Pro features: " + string.Join(", ", _lockedFeatures.Select(f => FeatureCatalog.Get(f).Name));
    }

    // ---------- Selection handling ----------
    private void OnContentTypeChanged()
    {
        var kind = ContentTypes.Value;
        ShowForm(GetOrCreateForm(kind));

        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            if (kind == ContentKind.Product && !IsRetail(Formats.Value))
                Formats.Value = BarcodeFormat.Ean13;
            else if (kind != ContentKind.Product && IsRetail(Formats.Value))
                Formats.Value = BarcodeFormat.QrCode;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnFormatChanged()
    {
        UpdateFormatState();

        if (!_syncingSelection && IsRetail(Formats.Value) && ContentTypes.Value != ContentKind.Product)
        {
            _syncingSelection = true;
            try { ContentTypes.Value = ContentKind.Product; }
            finally { _syncingSelection = false; }
        }

        SchedulePreview();
    }

    private void UpdateFormatState()
    {
        var format = Formats.Value;
        IsQrFormat = format == BarcodeFormat.QrCode;
        Design.SupportsAdvancedStyling = format.SupportsAdvancedStyling();
        Design.IsOneDimensional = !format.IsTwoDimensional();
        OnPropertyChanged(nameof(PreviewDescription));
    }

    private void ShowForm(ContentFormViewModel form)
    {
        if (!ReferenceEquals(form, CurrentForm))
        {
            CurrentForm.PropertyChanged -= OnFormPropertyChanged;
            CurrentForm = form;
            form.PropertyChanged += OnFormPropertyChanged;
        }

        if (ActiveForms.Count == 0) ActiveForms.Add(form);
        else if (!ReferenceEquals(ActiveForms[0], form)) ActiveForms[0] = form;

        RefreshFormLock();
        SchedulePreview();
    }

    private ContentFormViewModel GetOrCreateForm(ContentKind kind)
    {
        if (!_forms.TryGetValue(kind, out var form))
            _forms[kind] = form = ContentFormFactory.Create(kind, _protector);
        return form;
    }

    private async Task PrefillAsync(string payload)
    {
        QrContent content;
        try
        {
            content = _parser.Parse(payload);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not parse payload for editing");
            content = new TextContent(payload);
        }

        var kind = ContentKinds.FromContentType(content.Type);
        var form = GetOrCreateForm(kind);
        if (!form.TryLoad(content))
        {
            kind = ContentKind.Text;
            form = GetOrCreateForm(kind);
            form.TryLoad(new TextContent(payload));
        }

        if (content is ProductContent product)
        {
            _syncingSelection = true;
            try { Formats.Value = GuessRetailFormat(product.Code); }
            finally { _syncingSelection = false; }
            UpdateFormatState();
        }

        ContentTypes.Value = kind;
        ShowForm(form);

        if (content is SecureMessageContent)
        {
            await Dialogs.AlertAsync("Encrypted content",
                "Encrypted messages can't be edited. Type the message and a passphrase again to create a new secure code.");
        }
    }

    private static bool IsRetail(BarcodeFormat format) =>
        format is BarcodeFormat.Ean8 or BarcodeFormat.Ean13 or BarcodeFormat.UpcA or BarcodeFormat.UpcE;

    private static BarcodeFormat GuessRetailFormat(string code) => code.Trim().Length switch
    {
        7 or 8 => BarcodeFormat.Ean8,
        11 => BarcodeFormat.UpcA,
        _ => BarcodeFormat.Ean13
    };

    // ---------- Entitlements ----------
    private void OnEntitlementsChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        RefreshLocks();
        UpdateLockedFeatures();
    });

    private void RefreshLocks()
    {
        Formats.SetLocked(f => f != BarcodeFormat.QrCode && !_gate.IsUnlocked(PremiumFeature.AdvancedFormats));
        Sizes.SetLocked(s => s > FeatureCatalog.FreeMaxExportSize && !_gate.IsUnlocked(PremiumFeature.HighResolutionExport));
        ExportFormats.SetLocked(f =>
            (f == ExportFormat.Svg && !_gate.IsUnlocked(PremiumFeature.VectorExport)) ||
            (f == ExportFormat.Pdf && !_gate.IsUnlocked(PremiumFeature.PdfExport)));
        ContentTypes.SetLocked(k => ContentKinds.Get(k).RequiredFeature is { } feature && !_gate.IsUnlocked(feature));
        RefreshFormLock();
        Design.RefreshLocks();
    }

    private void RefreshFormLock() =>
        IsFormLocked = CurrentForm.Info.RequiredFeature is { } feature && !_gate.IsUnlocked(feature);

    private void ApplyDefaults(IAppSettingsService settings)
    {
        try
        {
            var current = settings.Current;
            if (current.DefaultFormat.IsWritable() && !IsRetail(current.DefaultFormat))
                Formats.Value = current.DefaultFormat;
            ErrorCorrections.Value = current.DefaultErrorCorrection;
            var size = Sizes.Items.Select(i => (int)i.Value!).Where(s => s <= current.DefaultExportSize).DefaultIfEmpty(1024).Max();
            Sizes.Value = size;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not read generation defaults");
        }
    }

    private void OnFormPropertyChanged(object? sender, PropertyChangedEventArgs e) => SchedulePreview();

    private void OnDesignPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignStudioViewModel.HasLogo))
            OnPropertyChanged(nameof(IsLogoForcingHigh));
    }

    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
}
