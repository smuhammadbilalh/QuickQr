using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickQr.Core.Content;
using QuickQr.Core.Generation;
using QuickQr.Core.History;
using QuickQr.Core.Scanning;
using QuickQr.Core.Security;

namespace QuickQr.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the domain and application services (no platform or I/O dependencies).</summary>
    public static IServiceCollection AddQuickQrCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IContentEncoder, ContentEncoder>();
        services.TryAddSingleton<IContentParser, ContentParser>();
        services.TryAddSingleton<IContentSummarizer, ContentSummarizer>();
        services.TryAddSingleton<IContentActionProvider, ContentActionProvider>();
        services.TryAddSingleton<IUrlSafetyAnalyzer, UrlSafetyAnalyzer>();
        services.TryAddSingleton<ISecurePayloadProtector, SecurePayloadProtector>();

        services.TryAddSingleton<IHistoryService, HistoryService>();
        services.TryAddSingleton<IQrGenerationService, QrGenerationService>();
        services.TryAddSingleton<IScanProcessingService, ScanProcessingService>();

        return services;
    }
}
