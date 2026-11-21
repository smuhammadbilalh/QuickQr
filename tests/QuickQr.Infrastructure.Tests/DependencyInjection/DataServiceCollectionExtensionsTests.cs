using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;
using QuickQr.Core.Rendering;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;
using QuickQr.Core.Updates;
using QuickQr.Infrastructure.DependencyInjection;
using QuickQr.Infrastructure.Licensing;
using QuickQr.Infrastructure.Persistence;
using QuickQr.Infrastructure.Security;
using QuickQr.Infrastructure.Tests.Fakes;
using QuickQr.Infrastructure.Updates;

namespace QuickQr.Infrastructure.Tests.DependencyInjection;

public sealed class DataServiceCollectionExtensionsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Registers_every_data_service_as_a_singleton()
    {
        var services = Services(_ => { });

        foreach (var type in new[]
        {
            typeof(IHistoryRepository), typeof(IHistoryExporter), typeof(IAppSettingsService), typeof(IPurchaseProvider),
            typeof(IEntitlementService), typeof(IBrandKitStore), typeof(IBatchInputParser), typeof(IBatchGenerator),
            typeof(IUpdateService), typeof(IUrlReputationService), typeof(TimeProvider)
        })
        {
            services.Where(d => d.ServiceType == type).Should().ContainSingle(type.Name)
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton, type.Name);
        }

        services.Should().ContainSingle(d => d.ServiceType == typeof(HttpClient) && Equals(d.ServiceKey, QuickQrHttp.ClientKey));
    }

    [Fact]
    public void Every_service_can_be_constructed()
    {
        using var provider = new TestServiceProvider(Services(_ => { }));

        provider.GetRequired<IHistoryRepository>().Should().BeOfType<SqliteHistoryRepository>()
            .Which.DatabasePath.Should().Be(_temp.File("quickqr.db3"));
        provider.GetRequired<IHistoryExporter>().Should().BeOfType<HistoryExporter>();
        provider.GetRequired<IAppSettingsService>().Current.Should().NotBeNull();
        provider.GetRequired<IPurchaseProvider>().Should().BeSameAs(provider.GetRequired<SimulatedPurchaseProvider>());
        provider.GetRequired<IEntitlementService>().Should().BeSameAs(provider.GetRequired<IEntitlementService>());
        provider.GetRequired<IBrandKitStore>().Should().NotBeNull();
        provider.GetRequired<IBatchInputParser>().Should().NotBeNull();
        provider.GetRequired<IBatchGenerator>().Should().NotBeNull();
        provider.GetRequired<IUpdateService>().Should().BeOfType<GitHubUpdateService>();
        provider.GetRequired<IUrlReputationService>().Should().BeOfType<SafeBrowsingReputationService>();
        ((HttpClient)provider.GetRequiredKeyedService(typeof(HttpClient), QuickQrHttp.ClientKey)).Timeout.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Host_registrations_win()
    {
        var store = Substitute.For<IPurchaseProvider>();
        var reputation = Substitute.For<IUrlReputationService>();

        var services = Services(s =>
        {
            s.AddSingleton(store);
            s.AddSingleton(reputation);
        });

        services.Single(d => d.ServiceType == typeof(IPurchaseProvider)).ImplementationInstance.Should().BeSameAs(store);
        services.Single(d => d.ServiceType == typeof(IUrlReputationService)).ImplementationInstance.Should().BeSameAs(reputation);
    }

    private ServiceCollection Services(Action<IServiceCollection> preRegister)
    {
        var services = new ServiceCollection();
        preRegister(services);
        services.AddSingleton<IOptions<QuickQrInfrastructureOptions>>(Options.Create(new QuickQrInfrastructureOptions()));
        services.AddSingleton<IAppInfoProvider>(new FakeAppInfoProvider(_temp.Path));
        services.AddSingleton<INetworkStatus>(new FakeNetworkStatus());
        services.AddSingleton<IKeyValueStore>(new InMemoryKeyValueStore());
        services.AddSingleton<ISecureKeyValueStore>(new InMemorySecureKeyValueStore());
        services.AddSingleton(Substitute.For<IBarcodeEncoder>());
        services.AddSingleton(Substitute.For<IBarcodeRenderer>());
        services.AddQuickQrData();
        return services;
    }
}
