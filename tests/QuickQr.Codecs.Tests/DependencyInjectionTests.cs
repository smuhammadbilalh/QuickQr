using Microsoft.Extensions.DependencyInjection;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Barcodes;
using QuickQr.Infrastructure.DependencyInjection;
using QuickQr.Infrastructure.Rendering;

namespace QuickQr.Codecs.Tests;

public class DependencyInjectionTests
{
    [Theory]
    [InlineData(typeof(IBarcodeEncoder), typeof(ZXingBarcodeEncoder))]
    [InlineData(typeof(IBarcodeImageDecoder), typeof(ZXingImageDecoder))]
    [InlineData(typeof(IBarcodeRenderer), typeof(SkiaBarcodeRenderer))]
    [InlineData(typeof(IPrintSheetGenerator), typeof(PdfPrintSheetGenerator))]
    public void Codecs_are_registered_as_singletons(Type service, Type implementation)
    {
        var services = new ServiceCollection().AddQuickQrCodecs();

        services.Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().BeEquivalentTo(new { ImplementationType = implementation, Lifetime = ServiceLifetime.Singleton });
    }

    [Fact]
    public void Registration_is_idempotent_and_respects_overrides()
    {
        var custom = Substitute.For<IBarcodeRenderer>();
        var services = new ServiceCollection();
        services.AddSingleton(custom);

        services.AddQuickQrCodecs().AddQuickQrCodecs();

        services.Where(d => d.ServiceType == typeof(IBarcodeRenderer)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(custom);
        services.Count(d => d.ServiceType == typeof(IBarcodeEncoder)).Should().Be(1);
    }
}
