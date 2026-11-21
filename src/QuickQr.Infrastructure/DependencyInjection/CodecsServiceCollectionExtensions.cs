using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Barcodes;
using QuickQr.Infrastructure.Rendering;

namespace QuickQr.Infrastructure.DependencyInjection;

// OWNER: codecs agent.
public static class CodecsServiceCollectionExtensions
{
    /// <summary>
    /// Barcode encoding/decoding (ZXing.Net) and rendering (SkiaSharp). All four services are stateless singletons;
    /// <see cref="PdfPrintSheetGenerator"/> also needs the <see cref="Core.Licensing.IEntitlementService"/> registered by the data services.
    /// </summary>
    public static IServiceCollection AddQuickQrCodecs(this IServiceCollection services)
    {
        services.TryAddSingleton<IBarcodeEncoder, ZXingBarcodeEncoder>();
        services.TryAddSingleton<IBarcodeImageDecoder, ZXingImageDecoder>();
        services.TryAddSingleton<IBarcodeRenderer, SkiaBarcodeRenderer>();
        services.TryAddSingleton<IPrintSheetGenerator, PdfPrintSheetGenerator>();
        return services;
    }
}
