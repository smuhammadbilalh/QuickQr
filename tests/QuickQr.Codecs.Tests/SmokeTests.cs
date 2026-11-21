namespace QuickQr.Codecs.Tests;

public class SmokeTests
{
    [Fact]
    public void Codecs_assembly_loads() =>
        typeof(QuickQr.Infrastructure.Barcodes.ZXingBarcodeEncoder).Assembly.Should().NotBeNull();
}
