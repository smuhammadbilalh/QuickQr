using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Scanning;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.Scan;

/// <summary>
/// Live scanner, scan-from-image and paste. A single scan pauses the camera and shows the result panel; continuous
/// mode (Pro) keeps scanning and collects unique codes in a session that can be exported as CSV.
/// </summary>
public sealed partial class ScanViewModel : ViewModelBase
{
    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    private readonly IScanProcessingService _processing;
    private readonly IScanFeedbackService _feedback;
    private readonly IContentActionExecutor _executor;
    private readonly IPremiumGate _premium;
    private readonly IMediaPickerService _mediaPicker;
    private readonly IBarcodeImageDecoder _imageDecoder;
    private readonly IClipboardService _clipboard;
    private readonly IShareService _share;
    private readonly IFileExportService _files;
    private readonly IAppSettingsService _settings;
    private readonly ICameraService _camera;
    private readonly ISharedContentInbox _inbox;
    private readonly IHapticsService _haptics;
    private readonly TimeProvider _clock;

    private readonly HashSet<string> _sessionKeys = new(StringComparer.Ordinal);
    private string? _lastCode;
    private DateTimeOffset _lastCodeAt = DateTimeOffset.MinValue;
    private bool _handlingDetection;
    private bool _isVisible;

    public ScanViewModel(
        IDialogService dialogs,
        ILogger<ScanViewModel> logger,
        IScanProcessingService processing,
        IScanFeedbackService feedback,
        IContentActionExecutor executor,
        IPremiumGate premium,
        IMediaPickerService mediaPicker,
        IBarcodeImageDecoder imageDecoder,
        IClipboardService clipboard,
        IShareService share,
        IFileExportService files,
        IAppSettingsService settings,
        ICameraService camera,
        ISharedContentInbox inbox,
        IHapticsService haptics,
        TimeProvider clock) : base(dialogs, logger)
    {
        _processing = processing;
        _feedback = feedback;
        _executor = executor;
        _premium = premium;
        _mediaPicker = mediaPicker;
        _imageDecoder = imageDecoder;
        _clipboard = clipboard;
        _share = share;
        _files = files;
        _settings = settings;
        _camera = camera;
        _inbox = inbox;
        _haptics = haptics;
        _clock = clock;

        Title = "Scan";
        SessionItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SessionCount));
            OnPropertyChanged(nameof(HasSession));
            OnPropertyChanged(nameof(SessionCountText));
        };
    }

    // ---------- Camera state ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCamera), nameof(ShowNoCamera), nameof(ShowPermissionDenied))]
    public partial bool IsCameraAvailable { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCamera), nameof(ShowNoCamera), nameof(ShowPermissionDenied))]
    public partial bool IsPermissionDenied { get; set; }

    /// <summary>Bound to <c>CameraView.CameraEnabled</c>: on while the page is visible.</summary>
    [ObservableProperty]
    public partial bool IsCameraEnabled { get; set; }

    /// <summary>Bound to <c>CameraView.PauseScanning</c>: detection pauses while a result is shown.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TorchGlyph), nameof(TorchDescription))]
    public partial bool IsTorchOn { get; set; }

    [ObservableProperty]
    public partial bool UseFrontCamera { get; set; }

    /// <summary>Reported by the camera (OneWayToSource).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasZoom))]
    public partial float MinZoom { get; set; } = 1f;

    /// <summary>Reported by the camera (OneWayToSource).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasZoom))]
    public partial float MaxZoom { get; set; } = 1f;

    /// <summary>Slider position 0..1, mapped onto the camera's zoom range.</summary>
    [ObservableProperty]
    public partial double ZoomLevel { get; set; }

    /// <summary>Bound to <c>CameraView.RequestZoomFactor</c>.</summary>
    [ObservableProperty]
    public partial float RequestedZoomFactor { get; set; } = 1f;

    public bool ShowCamera => IsCameraAvailable && !IsPermissionDenied;
    public bool ShowNoCamera => !IsCameraAvailable;
    public bool ShowPermissionDenied => IsCameraAvailable && IsPermissionDenied;
    public bool HasZoom => MinZoom > 0 && MaxZoom > MinZoom + 0.05f;
    public string TorchGlyph => IsTorchOn ? "\ue3e7" : "\ue3e6"; // flash_on / flash_off
    public string TorchDescription => IsTorchOn ? "Turn flashlight off" : "Turn flashlight on";

    // ---------- Result ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(ShowHint))]
    public partial ScanResultModel? Result { get; set; }

    public bool HasResult => Result is not null;

    // ---------- Continuous session ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint), nameof(HintText), nameof(ContinuousChipText), nameof(ShowLastSessionBanner))]
    public partial bool IsContinuous { get; set; }

    [ObservableProperty]
    public partial bool IsSessionListOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastSessionTitle), nameof(ShowLastSessionBanner))]
    public partial string? LastSessionTitle { get; set; }

    public ObservableCollection<SessionScanItem> SessionItems { get; } = [];
    public int SessionCount => SessionItems.Count;
    public bool HasSession => SessionItems.Count > 0;
    public string SessionCountText => SessionItems.Count == 1 ? "1 code" : $"{SessionItems.Count} codes";
    public bool HasLastSessionTitle => !string.IsNullOrEmpty(LastSessionTitle);
    public bool ShowLastSessionBanner => IsContinuous && HasLastSessionTitle;
    public bool IsContinuousLocked => !_premium.IsUnlocked(PremiumFeature.ContinuousScan);
    public string ContinuousChipText => IsContinuous ? "Continuous on" : "Continuous";

    public bool ShowHint => !HasResult;
    public string HintText => IsContinuous
        ? "Continuous mode: keep pointing at codes"
        : "Point at a QR code or barcode";

    // ---------- Lifecycle ----------

    public override async Task OnAppearingAsync()
    {
        _isVisible = true;
        _inbox.ContentAvailable -= OnSharedContentAvailable;
        _inbox.ContentAvailable += OnSharedContentAvailable;
        OnPropertyChanged(nameof(IsContinuousLocked));

        await StartCameraAsync();
        await ConsumeSharedContentAsync();
    }

    private async Task StartCameraAsync()
    {
        IsCameraAvailable = await _camera.IsCameraAvailableAsync();
        if (!IsCameraAvailable) return;

        var granted = await _camera.RequestPermissionAsync();
        IsPermissionDenied = !granted;
        IsCameraEnabled = granted && _isVisible;
    }

    public override Task OnDisappearingAsync()
    {
        _isVisible = false;
        _inbox.ContentAvailable -= OnSharedContentAvailable;
        IsTorchOn = false;
        IsCameraEnabled = false;
        return Task.CompletedTask;
    }

    partial void OnZoomLevelChanged(double value)
    {
        if (!HasZoom) return;
        var clamped = Math.Clamp(value, 0, 1);
        RequestedZoomFactor = (float)(MinZoom + (clamped * (MaxZoom - MinZoom)));
    }

    partial void OnUseFrontCameraChanged(bool value)
    {
        ZoomLevel = 0;
        IsTorchOn = false;
    }

    // ---------- Detection ----------

    /// <summary>Called by the page for every camera detection batch (on the main thread).</summary>
    public async Task OnCodesDetectedAsync(IReadOnlyList<DetectedCode> codes)
    {
        if (codes.Count == 0 || _handlingDetection || !_isVisible) return;
        if (!IsContinuous && (HasResult || IsPaused)) return;

        var now = _clock.GetUtcNow();
        _handlingDetection = true;
        try
        {
            if (IsContinuous)
            {
                foreach (var code in codes)
                {
                    if (_sessionKeys.Contains(KeyOf(code))) continue;
                    var outcome = await ProcessAsync(code, ScanSource.Camera);
                    if (outcome is not null) AddToSession(outcome, code);
                }

                return;
            }

            var first = codes[0];
            if (first.Text == _lastCode && now - _lastCodeAt < DuplicateWindow) return;
            _lastCode = first.Text;
            _lastCodeAt = now;

            IsPaused = true;
            var result = await ProcessAsync(first, ScanSource.Camera);
            if (result is null)
            {
                IsPaused = false;
                return;
            }

            await ShowResultAsync(result);
        }
        finally
        {
            _handlingDetection = false;
        }
    }

    private async Task<ScanOutcome?> ProcessAsync(DetectedCode code, ScanSource source)
    {
        try
        {
            var outcome = await _processing.ProcessAsync(code.Text, code.Format, source);
            _feedback.OnScanSucceeded();
            return outcome;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Processing a scanned code failed");
            await Dialogs.AlertAsync("Couldn't read that code", "Something went wrong while reading the code. Please try again.");
            return null;
        }
    }

    private async Task ShowResultAsync(ScanOutcome outcome)
    {
        var actions = outcome.Actions
            .Select(a => new ActionItem(a, new AsyncRelayCommand(() => ExecuteActionAsync(a, outcome.Content))))
            .ToList();
        Result = new ScanResultModel(outcome, actions);
        IsSessionListOpen = false;

        var settings = _settings.Current;
        if (settings.AutoCopyScans)
        {
            await _clipboard.SetTextAsync(outcome.Payload);
            await Dialogs.ToastAsync("Copied to clipboard");
        }

        // Only links the offline analyzer rates Safe (no warnings at all) may open without a tap.
        if (settings.AutoOpenSafeLinks && outcome.Safety is { Level: RiskLevel.Safe } && outcome.LinkTarget is not null)
        {
            var open = outcome.Actions.FirstOrDefault(a => a.Kind is ContentActionKind.OpenUrl or ContentActionKind.OpenStore);
            if (open is not null) await ExecuteActionAsync(open, outcome.Content);
        }
    }

    private async Task ExecuteActionAsync(ContentAction action, QrContent content)
    {
        try
        {
            await _executor.ExecuteAsync(action, content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Action {Action} failed", action.Kind);
            await Dialogs.AlertAsync("Something went wrong", ex.Message);
        }
    }

    private void AddToSession(ScanOutcome outcome, DetectedCode code)
    {
        if (!_sessionKeys.Add(KeyOf(code))) return;
        SessionItems.Insert(0, new SessionScanItem(outcome, _clock.GetUtcNow()));
        LastSessionTitle = outcome.Summary.Title;
        _haptics.Click();
    }

    private static string KeyOf(DetectedCode code) => ((int)code.Format).ToString(CultureInfo.InvariantCulture) + "|" + code.Text;

    // ---------- Commands: camera ----------

    [RelayCommand]
    private void ToggleTorch() => IsTorchOn = !IsTorchOn;

    [RelayCommand]
    private void FlipCamera() => UseFrontCamera = !UseFrontCamera;

    [RelayCommand]
    private void OpenAppSettings() => _camera.OpenAppSettings();

    [RelayCommand]
    private Task RetryCameraAsync() => StartCameraAsync();

    // ---------- Commands: other sources ----------

    [RelayCommand]
    private Task ScanFromImageAsync() => RunBusyAsync(async () =>
    {
        var file = await _mediaPicker.PickImageAsync();
        if (file is null) return;

        IReadOnlyList<DecodedBarcode> decoded;
        await using (var stream = await file.OpenReadAsync())
        {
            decoded = await _imageDecoder.DecodeAsync(stream);
        }

        await HandleDecodedImageAsync(decoded);
    }, "Couldn't read the image");

    private async Task HandleDecodedImageAsync(IReadOnlyList<DecodedBarcode> decoded)
    {
        var codes = decoded
            .Where(d => !string.IsNullOrEmpty(d.Text))
            .Select(d => new DetectedCode(d.Text, d.Format))
            .DistinctBy(KeyOf)
            .ToList();

        if (codes.Count == 0)
        {
            await Dialogs.AlertAsync("No code found", "QuickQr couldn't find a QR code or barcode in that image. Try a sharper image where the code fills more of the picture.");
            return;
        }

        if (IsContinuous)
        {
            foreach (var code in codes.Where(c => !_sessionKeys.Contains(KeyOf(c))))
            {
                if (await ProcessAsync(code, ScanSource.Image) is { } outcome)
                    AddToSession(outcome, code);
            }

            IsSessionListOpen = HasSession;
            return;
        }

        var chosen = codes[0];
        if (codes.Count > 1)
        {
            var labels = codes.Select((c, i) => $"{i + 1}. {FormatName(c.Format)}: {Shorten(c.Text, 48)}").ToArray();
            var choice = await Dialogs.ActionSheetAsync($"{codes.Count} codes found", "Cancel", null, labels);
            if (choice is null) return;
            var index = Array.IndexOf(labels, choice);
            if (index < 0) return;
            chosen = codes[index];
        }

        await ProcessAndShowAsync(chosen, ScanSource.Image);
    }

    [RelayCommand]
    private Task PasteFromClipboardAsync() => RunBusyAsync(async () =>
    {
        var text = await _clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await Dialogs.AlertAsync("Clipboard is empty", "Copy a link or the text of a code first, then tap Paste.");
            return;
        }

        var code = new DetectedCode(text.Trim(), BarcodeFormat.Unknown);
        if (IsContinuous)
        {
            if (!_sessionKeys.Contains(KeyOf(code)) && await ProcessAsync(code, ScanSource.Clipboard) is { } outcome)
                AddToSession(outcome, code);
            IsSessionListOpen = HasSession;
            return;
        }

        await ProcessAndShowAsync(code, ScanSource.Clipboard);
    });

    private async Task ProcessAndShowAsync(DetectedCode code, ScanSource source)
    {
        IsPaused = true;
        var outcome = await ProcessAsync(code, source);
        if (outcome is null)
        {
            IsPaused = false;
            return;
        }

        await ShowResultAsync(outcome);
    }

    private void OnSharedContentAvailable(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await ConsumeSharedContentAsync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "Shared content could not be processed"); }
        });

    private async Task ConsumeSharedContentAsync()
    {
        if (!_isVisible || _inbox.Take() is not { } shared) return;

        if (shared.ImageBytes is { Length: > 0 } bytes)
        {
            await RunBusyAsync(async () =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                await HandleDecodedImageAsync(await _imageDecoder.DecodeAsync(stream));
            }, "Couldn't read the shared image");
        }
        else if (!string.IsNullOrWhiteSpace(shared.Text))
        {
            await ProcessAndShowAsync(new DetectedCode(shared.Text.Trim(), BarcodeFormat.Unknown), ScanSource.Clipboard);
        }
    }

    // ---------- Commands: result panel ----------

    [RelayCommand]
    private void DismissResult()
    {
        Result = null;
        // Ignore the code that is probably still in front of the camera for a moment.
        _lastCodeAt = _clock.GetUtcNow();
        IsPaused = false;
    }

    [RelayCommand]
    private async Task CopyPayloadAsync()
    {
        if (Result is null) return;
        await _clipboard.SetTextAsync(Result.Payload);
        _haptics.Click();
        await Dialogs.ToastAsync("Copied to clipboard");
    }

    [RelayCommand]
    private async Task SharePayloadAsync()
    {
        if (Result is null) return;
        await _share.ShareTextAsync(Result.Title, Result.Payload);
    }

    // ---------- Commands: continuous session ----------

    [RelayCommand]
    private async Task ToggleContinuousAsync()
    {
        if (IsContinuous)
        {
            IsContinuous = false;
            IsSessionListOpen = false;
            return;
        }

        if (!await _premium.EnsureAsync(PremiumFeature.ContinuousScan))
        {
            OnPropertyChanged(nameof(IsContinuousLocked));
            return;
        }

        OnPropertyChanged(nameof(IsContinuousLocked));
        Result = null;
        IsContinuous = true;
        IsPaused = false;
        await Dialogs.ToastAsync("Continuous scanning on: every new code is added to the session");
    }

    [RelayCommand]
    private void ToggleSessionList() => IsSessionListOpen = !IsSessionListOpen && HasSession;

    [RelayCommand]
    private Task ExportSessionAsync() => RunBusyAsync(async () =>
    {
        if (!HasSession) return;
        using var stream = new MemoryStream(BuildSessionCsv(), writable: false);
        var result = await _files.SaveFileAsync(stream, SessionFileName());
        if (result.IsSuccess)
            await Dialogs.ToastAsync("Session saved");
        else if (result.Error.Code != ErrorCodes.Cancelled)
            await Dialogs.AlertAsync("Couldn't save", result.Error.Message);
    }, "Couldn't export the session");

    [RelayCommand]
    private Task ShareSessionAsync() => RunBusyAsync(async () =>
    {
        if (!HasSession) return;
        var path = await _files.WriteTempFileAsync(BuildSessionCsv(), SessionFileName());
        await _share.ShareFileAsync("Scan session", path, "text/csv");
    }, "Couldn't share the session");

    [RelayCommand]
    private async Task ClearSessionAsync()
    {
        if (!HasSession) return;
        if (!await Dialogs.ConfirmAsync("Clear session?", $"Remove the {SessionCountText} collected in this session? They stay in your history.", "Clear", "Cancel"))
            return;

        SessionItems.Clear();
        _sessionKeys.Clear();
        LastSessionTitle = null;
        IsSessionListOpen = false;
    }

    internal byte[] BuildSessionCsv()
    {
        var sb = new StringBuilder("value,format,type,time\r\n");
        foreach (var item in SessionItems.Reverse())
        {
            sb.Append(Csv(item.Value)).Append(',')
              .Append(Csv(item.FormatName)).Append(',')
              .Append(Csv(item.TypeName)).Append(',')
              .Append(item.ScannedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))
              .Append("\r\n");
        }

        // UTF-8 with BOM so spreadsheet apps detect the encoding.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private string SessionFileName() =>
        $"quickqr-scan-session-{_clock.GetLocalNow():yyyyMMdd-HHmm}.csv";

    /// <summary>RFC 4180 quoting plus a guard against spreadsheet formula injection.</summary>
    internal static string Csv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string FormatName(BarcodeFormat format) => format == BarcodeFormat.Unknown ? "Code" : format.DisplayName();

    private static string Shorten(string text, int max)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= max ? single : single[..(max - 1)] + "…";
    }
}
