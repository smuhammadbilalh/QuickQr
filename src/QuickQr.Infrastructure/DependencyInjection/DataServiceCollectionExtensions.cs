using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickQr.Core.Batch;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;
using QuickQr.Core.Updates;
using QuickQr.Infrastructure.Batch;
using QuickQr.Infrastructure.Branding;
using QuickQr.Infrastructure.Licensing;
using QuickQr.Infrastructure.Persistence;
using QuickQr.Infrastructure.Security;
using QuickQr.Infrastructure.Settings;
using QuickQr.Infrastructure.Updates;

namespace QuickQr.Infrastructure.DependencyInjection;

// OWNER: data agent.
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Persistence, settings, licensing, batch, updates and online services. Every registration uses "try" semantics:
    /// register a platform store <see cref="IPurchaseProvider"/> (or a Web Risk <see cref="IUrlReputationService"/>)
    /// before calling this to replace the defaults.
    /// </summary>
    public static IServiceCollection AddQuickQrData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<QuickQrInfrastructureOptions>();
        services.TryAddSingleton(TimeProvider.System);

        // One shared HttpClient (default platform handler, 20 s timeout) for the online services.
        services.TryAddKeyedSingleton(QuickQrHttp.ClientKey, static (_, _) => QuickQrHttp.CreateClient());

        services.TryAddSingleton<SqliteHistoryRepository>();
        services.TryAddSingleton<IHistoryRepository>(static sp => sp.GetRequiredService<SqliteHistoryRepository>());
        services.TryAddSingleton<IHistoryExporter, HistoryExporter>();
        services.TryAddSingleton<IAppSettingsService, AppSettingsService>();

        services.TryAddSingleton<SimulatedPurchaseProvider>();
        services.TryAddSingleton<IPurchaseProvider>(static sp => sp.GetRequiredService<SimulatedPurchaseProvider>());
        services.TryAddSingleton<IEntitlementService, EntitlementService>();

        services.TryAddSingleton<IBrandKitStore, FileBrandKitStore>();
        services.TryAddSingleton<IBatchInputParser, BatchInputParser>();
        services.TryAddSingleton<IBatchGenerator, ZipBatchGenerator>();

        services.TryAddSingleton<IUpdateService, GitHubUpdateService>();
        services.TryAddSingleton<IUrlReputationService, SafeBrowsingReputationService>();

        return services;
    }
}
