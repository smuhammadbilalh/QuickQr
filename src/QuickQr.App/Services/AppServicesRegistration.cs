using QuickQr.Services.Actions;
using QuickQr.Services.Lifecycle;
#if !DEBUG && !SIMULATED_STORE
using QuickQr.Core.Licensing;
using QuickQr.Services.Billing;
#endif

namespace QuickQr.Services;

// OWNER: scan-and-manage agent. Registers IDialogService, INavigationService, IShareService, IFileExportService,
// IMediaPickerService, IClipboardService, IHapticsService, IScanFeedbackService, IContentActionExecutor,
// IThemeService, IAppLockService, IAppLifecycleHook(s) and the store IPurchaseProvider (Release builds).
public static class AppServicesRegistration
{
    public static IServiceCollection AddQuickQrAppServices(this IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IShareService, ShareService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IHapticsService, HapticsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IMediaPickerService, MediaPickerService>();
        services.AddSingleton<IFileExportService, FileExportService>();
        services.AddSingleton<IScanFeedbackService, ScanFeedbackService>();
        services.AddSingleton<IExternalLauncher, ExternalLauncher>();
        services.AddSingleton<ICameraService, CameraService>();
        services.AddSingleton<ISharedContentInbox, SharedContentInbox>();
        services.AddSingleton<IWifiConnector, WifiConnector>();
        services.AddSingleton<IContentActionExecutor, ContentActionExecutor>();
        services.AddSingleton<IAppLockService, AppLockService>();

        // Lifecycle hooks run in registration order: lock first so nothing shows above the lock screen.
        services.AddSingleton<AppLockLifecycleHook>();
        services.AddSingleton<IAppLifecycleHook>(static sp => sp.GetRequiredService<AppLockLifecycleHook>());
        services.AddSingleton<IAppLifecycleHook, SharedContentLifecycleHook>();
        services.AddSingleton<IAppLifecycleHook, UpdateCheckLifecycleHook>();

#if !DEBUG && !SIMULATED_STORE
        // Real store billing in Release; Debug and dev (-p:QuickQrSimulatedStore=true) builds use the simulated store
        // registered by infrastructure (TryAdd).
        services.AddSingleton<IPurchaseProvider, StorePurchaseProvider>();
#endif
        return services;
    }
}
