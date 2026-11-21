using System.Globalization;
using BarcodeScanning;
using CoreFormat = QuickQr.Core.Barcodes.BarcodeFormat;

namespace QuickQr.Features.Scan;

/// <summary>Maps BarcodeScanning.Native.Maui symbologies to Core <see cref="CoreFormat"/>.</summary>
public static class ScannerFormatMap
{
    public static CoreFormat ToCore(BarcodeFormats format) => format switch
    {
        BarcodeFormats.QRCode or BarcodeFormats.MicroQR => CoreFormat.QrCode,
        BarcodeFormats.DataMatrix => CoreFormat.DataMatrix,
        BarcodeFormats.Aztec => CoreFormat.Aztec,
        BarcodeFormats.Pdf417 or BarcodeFormats.MicroPdf417 => CoreFormat.Pdf417,
        BarcodeFormats.Code128 => CoreFormat.Code128,
        BarcodeFormats.Code39 => CoreFormat.Code39,
        BarcodeFormats.Code93 => CoreFormat.Code93,
        BarcodeFormats.CodaBar => CoreFormat.Codabar,
        BarcodeFormats.Ean13 or BarcodeFormats.ISBN => CoreFormat.Ean13,
        BarcodeFormats.Ean8 => CoreFormat.Ean8,
        BarcodeFormats.Upca => CoreFormat.UpcA,
        BarcodeFormats.Upce => CoreFormat.UpcE,
        BarcodeFormats.Itf or BarcodeFormats.I2OF5 => CoreFormat.Itf,
        BarcodeFormats.GS1DataBar => CoreFormat.Rss14,
        BarcodeFormats.MaxiCode => CoreFormat.MaxiCode,
        _ => CoreFormat.Unknown,
    };

    public static IReadOnlyList<DetectedCode> ToDetected(IEnumerable<BarcodeResult>? results) =>
        results is null
            ? []
            : results
                .Select(r => new DetectedCode(string.IsNullOrEmpty(r.RawValue) ? r.DisplayValue ?? string.Empty : r.RawValue, ToCore(r.BarcodeFormat)))
                .Where(c => c.Text.Length > 0)
                .ToList();
}

/// <summary>True → front camera.</summary>
public sealed class FrontCameraToFacingConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? CameraFacing.Front : CameraFacing.Back;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CameraFacing.Front;
}
