using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickQr.Features.Batch;
using QuickQr.Features.Generate;
using QuickQr.Features.Premium;
using QuickQr.Services;

namespace QuickQr.Features;

// OWNER: create-experience agent. Registers pages, view models, routes and services for Create, Batch and Premium.
public static class CreateFeatureRegistration
{
    public static IServiceCollection AddCreateFeatures(this IServiceCollection services)
    {
        // Paywall gate: one instance shared by every premium entry point and the paywall view model.
        services.AddSingleton<PremiumGate>();
        services.AddSingleton<IPremiumGate>(sp => sp.GetRequiredService<PremiumGate>());

        // Batch output is written to the app cache before sharing/saving.
        services.TryAddSingleton(FileSystem.Current);

        services.AddTransient<GeneratePage>();
        services.AddTransient<GenerateViewModel>();
        services.AddTransient<DesignStudioViewModel>();
        services.AddTransient<BatchPage>();
        services.AddTransient<BatchViewModel>();
        services.AddTransient<PaywallPage>();
        services.AddTransient<PaywallViewModel>();

        Routing.RegisterRoute(Routes.Batch, typeof(BatchPage));
        Routing.RegisterRoute(Routes.Paywall, typeof(PaywallPage));
        return services;
    }
}
