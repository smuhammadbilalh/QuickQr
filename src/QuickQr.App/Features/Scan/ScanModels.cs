using CommunityToolkit.Mvvm.Input;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Scanning;
using QuickQr.Core.Security;
using QuickQr.Services.Presentation;

namespace QuickQr.Features.Scan;

/// <summary>A code found by the camera or in an image, independent of the scanning library.</summary>
public sealed record DetectedCode(string Text, BarcodeFormat Format);

/// <summary>A suggested action rendered as a button; the command runs it through the content action executor.</summary>
public sealed class ActionItem(ContentAction action, IAsyncRelayCommand command)
{
    public ContentAction Action { get; } = action;
    public string Label => Action.Label;
    public string Icon => Action.IconGlyph;
    public bool IsPrimary => Action.IsPrimary;
    public IAsyncRelayCommand Command { get; } = command;
}

public sealed class SafetySignalItem(SafetySignal signal)
{
    public string Description { get; } = signal.Description;
    public string Glyph { get; } = signal.Severity switch
    {
        RiskLevel.High => Glyphs.Error,
        RiskLevel.Medium => Glyphs.Warning,
        _ => Glyphs.Info,
    };
}

/// <summary>Link-safety card for a scanned link. Wording never claims a link is "safe" — only that nothing was found.</summary>
public sealed class SafetyCardModel
{
    public SafetyCardModel(UrlSafetyReport report)
    {
        Level = report.Level;
        (HostPrefix, HostRegistrable) = RegistrableDomain.Split(report.DisplayHost ?? TryHost(report.Url));
        Signals = report.Signals.OrderByDescending(s => s.Severity).Select(s => new SafetySignalItem(s)).ToList();

        (Headline, Description, Glyph) = Level switch
        {
            RiskLevel.High => ("Warning: possible scam link", "This link shows strong signs of phishing. Don't enter passwords or payment details.", Glyphs.Error),
            RiskLevel.Medium => ("Check this link carefully", "Some things about this link are unusual. Make sure you trust where the code came from.", Glyphs.Warning),
            RiskLevel.Low => ("Minor notes", "Nothing alarming, but keep these in mind:", Glyphs.Info),
            _ => ("No warnings found", "QuickQr's offline checks found nothing unusual. Still, only open links from sources you trust.", Glyphs.Shield),
        };
    }

    public RiskLevel Level { get; }
    public bool IsSafe => Level == RiskLevel.Safe;
    public bool IsLow => Level == RiskLevel.Low;
    public bool IsMedium => Level == RiskLevel.Medium;
    public bool IsHigh => Level == RiskLevel.High;
    public string Headline { get; }
    public string Description { get; }
    public string Glyph { get; }
    /// <summary>Sub-domains before the registrable domain, e.g. "login.secure.".</summary>
    public string HostPrefix { get; }
    /// <summary>The part users must check, e.g. "example.co.uk" (rendered bold).</summary>
    public string HostRegistrable { get; }
    public bool HasHost => HostRegistrable.Length > 0;
    public IReadOnlyList<SafetySignalItem> Signals { get; }
    public bool HasSignals => Signals.Count > 0;

    private static string? TryHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.IdnHost : null;
}

/// <summary>Everything the result panel shows for one scan.</summary>
public sealed class ScanResultModel
{
    public ScanResultModel(ScanOutcome outcome, IReadOnlyList<ActionItem> actions)
    {
        Outcome = outcome;
        Title = outcome.Summary.Title;
        Subtitle = outcome.Summary.Subtitle;
        Icon = string.IsNullOrEmpty(outcome.Summary.IconGlyph) ? Glyphs.For(outcome.Content.Type) : outcome.Summary.IconGlyph;
        TypeName = ContentTypeNames.For(outcome.Content.Type);
        FormatName = outcome.Format == BarcodeFormat.Unknown ? "Text" : outcome.Format.DisplayName();
        Payload = outcome.Payload;
        Safety = outcome.Safety is null ? null : new SafetyCardModel(outcome.Safety);
        PrimaryAction = actions.FirstOrDefault(a => a.IsPrimary) ?? actions.FirstOrDefault();
        SecondaryActions = actions.Where(a => !ReferenceEquals(a, PrimaryAction)).ToList();
    }

    public ScanOutcome Outcome { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public string Icon { get; }
    public string TypeName { get; }
    public string FormatName { get; }
    public string Meta => $"{TypeName} · {FormatName}";
    public string Payload { get; }
    public SafetyCardModel? Safety { get; }
    public bool HasSafety => Safety is not null;
    public ActionItem? PrimaryAction { get; }
    public bool HasPrimaryAction => PrimaryAction is not null;
    public IReadOnlyList<ActionItem> SecondaryActions { get; }
}

/// <summary>One unique code collected in a continuous-scan session.</summary>
public sealed class SessionScanItem(ScanOutcome outcome, DateTimeOffset scannedAt)
{
    public ScanOutcome Outcome { get; } = outcome;
    public string Value => Outcome.Payload;
    public string Title => Outcome.Summary.Title;
    public string Icon => string.IsNullOrEmpty(Outcome.Summary.IconGlyph) ? Glyphs.For(Outcome.Content.Type) : Outcome.Summary.IconGlyph;
    public BarcodeFormat Format => Outcome.Format;
    public string FormatName => Outcome.Format == BarcodeFormat.Unknown ? "Text" : Outcome.Format.DisplayName();
    public string TypeName => ContentTypeNames.For(Outcome.Content.Type);
    public DateTimeOffset ScannedAt { get; } = scannedAt;
    public string Meta => $"{TypeName} · {FormatName} · {ScannedAt.ToLocalTime():HH:mm:ss}";
}
