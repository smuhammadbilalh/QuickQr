using QuickQr.Core.Barcodes;
using ZxFormat = ZXing.BarcodeFormat;

namespace QuickQr.Infrastructure.Barcodes;

/// <summary>Maps QuickQr symbologies to ZXing.Net formats and back.</summary>
internal static class ZXingFormatMap
{
    /// <summary>Every symbology QuickQr can read (all known formats).</summary>
    public static IReadOnlyList<BarcodeFormat> Readable { get; } =
        Enum.GetValues<BarcodeFormat>().Where(f => f != BarcodeFormat.Unknown).ToArray();

    public static ZxFormat? ToZXing(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QrCode => ZxFormat.QR_CODE,
        BarcodeFormat.DataMatrix => ZxFormat.DATA_MATRIX,
        BarcodeFormat.Aztec => ZxFormat.AZTEC,
        BarcodeFormat.Pdf417 => ZxFormat.PDF_417,
        BarcodeFormat.Code128 => ZxFormat.CODE_128,
        BarcodeFormat.Code39 => ZxFormat.CODE_39,
        BarcodeFormat.Code93 => ZxFormat.CODE_93,
        BarcodeFormat.Ean8 => ZxFormat.EAN_8,
        BarcodeFormat.Ean13 => ZxFormat.EAN_13,
        BarcodeFormat.UpcA => ZxFormat.UPC_A,
        BarcodeFormat.UpcE => ZxFormat.UPC_E,
        BarcodeFormat.Itf => ZxFormat.ITF,
        BarcodeFormat.Codabar => ZxFormat.CODABAR,
        BarcodeFormat.MaxiCode => ZxFormat.MAXICODE,
        BarcodeFormat.Rss14 => ZxFormat.RSS_14,
        BarcodeFormat.RssExpanded => ZxFormat.RSS_EXPANDED,
        _ => null
    };

    public static BarcodeFormat FromZXing(ZxFormat format) => format switch
    {
        ZxFormat.QR_CODE => BarcodeFormat.QrCode,
        ZxFormat.DATA_MATRIX => BarcodeFormat.DataMatrix,
        ZxFormat.AZTEC => BarcodeFormat.Aztec,
        ZxFormat.PDF_417 => BarcodeFormat.Pdf417,
        ZxFormat.CODE_128 => BarcodeFormat.Code128,
        ZxFormat.CODE_39 => BarcodeFormat.Code39,
        ZxFormat.CODE_93 => BarcodeFormat.Code93,
        ZxFormat.EAN_8 => BarcodeFormat.Ean8,
        ZxFormat.EAN_13 => BarcodeFormat.Ean13,
        ZxFormat.UPC_A => BarcodeFormat.UpcA,
        ZxFormat.UPC_E => BarcodeFormat.UpcE,
        ZxFormat.ITF => BarcodeFormat.Itf,
        ZxFormat.CODABAR => BarcodeFormat.Codabar,
        ZxFormat.MAXICODE => BarcodeFormat.MaxiCode,
        ZxFormat.RSS_14 => BarcodeFormat.Rss14,
        ZxFormat.RSS_EXPANDED => BarcodeFormat.RssExpanded,
        _ => BarcodeFormat.Unknown
    };
}
