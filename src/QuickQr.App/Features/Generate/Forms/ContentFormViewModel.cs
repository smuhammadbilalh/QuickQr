using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickQr.Core.Content;

namespace QuickQr.Features.Generate;

/// <summary>
/// Bindable fields for one content type. <see cref="BuildContent"/> turns them into a typed <see cref="QrContent"/>;
/// standards validation happens in <see cref="IContentEncoder"/>, so forms only check what they parse themselves
/// (numbers, dates, passphrases). Any property change notifies the page, which debounces a new preview.
/// </summary>
public abstract partial class ContentFormViewModel : ObservableObject
{
    public abstract ContentKind Kind { get; }

    public ContentKindInfo Info => ContentKinds.Get(Kind);

    /// <summary>True while nothing meaningful has been entered (no preview, no validation message).</summary>
    public abstract bool IsEmpty { get; }

    /// <summary>A message for input the form cannot convert (e.g. a malformed number); null when convertible.</summary>
    public virtual string? Validate() => null;

    public abstract QrContent BuildContent();

    /// <summary>Async variant for forms with expensive conversion (Secure message key derivation).</summary>
    public virtual Task<QrContent> BuildContentAsync(CancellationToken cancellationToken) => Task.FromResult(BuildContent());

    /// <summary>Prefills the form from parsed content. Returns false when the content does not belong to this form.</summary>
    public abstract bool TryLoad(QrContent content);

    protected static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static string Required(string? value) => value?.Trim() ?? string.Empty;

    protected static bool IsBlank(params string?[] values) => values.All(string.IsNullOrWhiteSpace);

    /// <summary>Parses a number typed with either '.' or the current culture's separator.</summary>
    protected static bool TryParseDecimal(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) ||
            decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out invariant))
        {
            value = invariant;
            return true;
        }

        return false;
    }

    protected static bool TryParseDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    protected static string Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    protected static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
