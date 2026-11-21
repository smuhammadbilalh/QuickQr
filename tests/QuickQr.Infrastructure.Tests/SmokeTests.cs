namespace QuickQr.Infrastructure.Tests;

public class SmokeTests
{
    [Fact]
    public void Infrastructure_assembly_loads() =>
        typeof(QuickQr.Infrastructure.QuickQrInfrastructureOptions).Assembly.Should().NotBeNull();
}
