using Microsoft.Extensions.DependencyInjection;

namespace QuickQr.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers every infrastructure adapter. The host must also register the platform ports from
    /// <c>QuickQr.Core.Platform</c> (IKeyValueStore, ISecureKeyValueStore, IAppInfoProvider, INetworkStatus).
    /// </summary>
    public static IServiceCollection AddQuickQrInfrastructure(
        this IServiceCollection services,
        Action<QuickQrInfrastructureOptions>? configure = null)
    {
        var options = services.AddOptions<QuickQrInfrastructureOptions>();
        if (configure is not null) options.Configure(configure);

        services.AddQuickQrCodecs();
        services.AddQuickQrData();
        return services;
    }
}
