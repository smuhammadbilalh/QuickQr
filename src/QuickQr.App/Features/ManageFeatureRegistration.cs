using QuickQr.Features.About;
using QuickQr.Features.History;
using QuickQr.Features.Scan;
using QuickQr.Features.Settings;

namespace QuickQr.Features;

// OWNER: scan-and-manage agent. Registers pages, view models and routes for Scan, History, Settings and About.
public static class ManageFeatureRegistration
{
    public static IServiceCollection AddScanAndManageFeatures(this IServiceCollection services)
    {
        // Root tabs (resolved by Shell's ContentTemplate).
        services.AddTransient<ScanPage>();
        services.AddTransient<ScanViewModel>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();

        // Pushed pages.
        services.AddTransient<HistoryDetailPage>();
        services.AddTransient<HistoryDetailViewModel>();
        services.AddTransient<AboutPage>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<LicensesPage>();
        services.AddTransient<LicensesViewModel>();

        // Modal lock screen (pushed by AppLockLifecycleHook).
        services.AddTransient<LockPage>();
        services.AddTransient<LockViewModel>();

        Routing.RegisterRoute(Routes.HistoryDetail, typeof(HistoryDetailPage));
        Routing.RegisterRoute(Routes.About, typeof(AboutPage));
        Routing.RegisterRoute(AboutRoutes.Licenses, typeof(LicensesPage));
        return services;
    }
}
