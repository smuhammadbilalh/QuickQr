using Microsoft.Extensions.Logging;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Core.Serialization;
using QuickQr.Core.Settings;

namespace QuickQr.Core.Generation;

public enum GenerationMode
{
    /// <summary>Live preview while editing: premium styles are rendered (try-before-you-buy), nothing is saved.</summary>
    Preview = 0,
    /// <summary>Final output for save/share/export: entitlements are enforced and history is recorded.</summary>
    Export = 1
}

public sealed record GenerationRequest(
    QrContent Content,
    BarcodeFormat Format = BarcodeFormat.QrCode,
    ErrorCorrectionLevel ErrorCorrection = ErrorCorrectionLevel.Medium,
    QrStyle? Style = null,
    RenderOptions? RenderOptions = null);

public sealed record GeneratedCode(
    string Payload,
    QrContent Content,
    BarcodeMatrix Matrix,
    RenderedImage Image,
    HistoryEntry? HistoryEntry);

public interface IQrGenerationService
{
    Task<Result<GeneratedCode>> GenerateAsync(GenerationRequest request, GenerationMode mode, CancellationToken cancellationToken = default);

    /// <summary>Premium features the request would use that the current tier does not unlock.</summary>
    IReadOnlyList<PremiumFeature> GetLockedFeatures(GenerationRequest request);
}

/// <summary>Orchestrates content encoding → barcode encoding → rendering → history.</summary>
public sealed class QrGenerationService(
    IContentEncoder contentEncoder,
    IBarcodeEncoder barcodeEncoder,
    IBarcodeRenderer renderer,
    IEntitlementService entitlements,
    IHistoryService history,
    IAppSettingsService settings,
    ILogger<QrGenerationService> logger) : IQrGenerationService
{
    public async Task<Result<GeneratedCode>> GenerateAsync(GenerationRequest request, GenerationMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var style = request.Style ?? QrStyle.Default;
        var options = request.RenderOptions ?? new RenderOptions();

        if (mode == GenerationMode.Export)
        {
            var locked = GetLockedFeatures(request);
            if (locked.Count > 0)
            {
                var names = string.Join(", ", locked.Select(f => FeatureCatalog.Get(f).Name));
                return Error.FeatureLocked($"Upgrade to use: {names}.");
            }
        }

        var payload = contentEncoder.Encode(request.Content);
        if (payload.IsFailure) return payload.Error;

        // A centre logo hides modules; maximum error correction keeps the code readable.
        var ecl = style.Logo is not null && request.Format == BarcodeFormat.QrCode
            ? ErrorCorrectionLevel.High
            : request.ErrorCorrection;

        var matrix = barcodeEncoder.Encode(new BarcodeEncodeRequest(payload.Value, request.Format, ecl));
        if (matrix.IsFailure) return matrix.Error;

        var image = await renderer.RenderAsync(matrix.Value, style, options, cancellationToken).ConfigureAwait(false);
        if (image.IsFailure) return image.Error;

        HistoryEntry? entry = null;
        if (mode == GenerationMode.Export && settings.Current.SaveGeneratedToHistory)
        {
            try
            {
                var styleJson = CoreJson.SerializeStyle(style);
                entry = await history.RecordGeneratedAsync(payload.Value, request.Content, request.Format, styleJson, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // History is best effort: never fail an export because the database is unavailable.
                logger.LogWarning(ex, "Failed to record generated code in history");
            }
        }

        return new GeneratedCode(payload.Value, request.Content, matrix.Value, image.Value, entry);
    }

    public IReadOnlyList<PremiumFeature> GetLockedFeatures(GenerationRequest request)
    {
        var used = FeatureCatalog.FeaturesUsedBy(request.Style ?? QrStyle.Default, request.RenderOptions);
        var locked = used.Where(f => !entitlements.HasFeature(f)).ToList();

        if (request.Format != BarcodeFormat.QrCode && !entitlements.HasFeature(PremiumFeature.AdvancedFormats))
            locked.Add(PremiumFeature.AdvancedFormats);

        if (request.Content is SecureMessageContent && !entitlements.HasFeature(PremiumFeature.SecureQr))
            locked.Add(PremiumFeature.SecureQr);

        return locked;
    }
}
