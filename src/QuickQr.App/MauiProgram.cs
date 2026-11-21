using BarcodeScanning;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using QuickQr.Core.DependencyInjection;
using QuickQr.Core.Platform;
using QuickQr.Features;
using QuickQr.Infrastructure.DependencyInjection;
using QuickQr.Services;
using QuickQr.Services.Platform;

namespace QuickQr;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit(options => options.SetShouldEnableSnackbarOnWindows(true))
            .UseBarcodeScanning()
            .ConfigureEssentials(essentials => essentials
                .AddAppAction(AppActionIds.Scan, "Scan code")
                .AddAppAction(AppActionIds.Create, "Create code")
                .OnAppAction(OnAppAction))
            .ConfigureFonts(fonts =>
            {
                // Material Symbols Rounded (Material 3), subset to the glyphs the app uses. The alias stays
                // "MaterialIcons" so XAML/C# glyph references are unchanged. Text uses the platform font.
                fonts.AddFont("MaterialSymbolsRounded.ttf", "MaterialIcons");
                fonts.AddFont("MaterialSymbolsRoundedFilled.ttf", "MaterialIconsFilled"); // filled star for favourites
            });

        // 1. Platform ports required by Core/Infrastructure.
        builder.Services.AddSingleton<IKeyValueStore, PreferencesKeyValueStore>();
        builder.Services.AddSingleton<ISecureKeyValueStore, SecureStorageKeyValueStore>();
        builder.Services.AddSingleton<IAppInfoProvider, MauiAppInfoProvider>();
        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();

        // 2. App services (dialogs, navigation, sharing, store billing, ...). Registered before infrastructure so
        //    a real store IPurchaseProvider wins over the simulated one (infrastructure uses TryAdd).
        builder.Services.AddQuickQrAppServices();

        // 3. Domain + infrastructure.
        builder.Services.AddQuickQrCore();
        builder.Services.AddQuickQrInfrastructure(options =>
        {
#if DEBUG || SIMULATED_STORE
            options.UseSimulatedStore = true;
#endif
            options.SafeBrowsingApiKey = AppConfiguration.SafeBrowsingApiKey;
        });

        // 4. Feature modules (pages, view models, routes).
        builder.Services.AddCreateFeatures();
        builder.Services.AddScanAndManageFeatures();

        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        return builder.Build();
    }

    private static void OnAppAction(AppAction action)
    {
        var route = action.Id switch
        {
            AppActionIds.Scan => Routes.Scan,
            AppActionIds.Create => Routes.Create,
            _ => null
        };
        if (route is null) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync($"//{route}");
        });
    }
}

public static class AppActionIds
{
    public const string Scan = "quickqr.scan";
    public const string Create = "quickqr.create";
}

/// <summary>Build-time configuration. Secrets are injected by CI (never commit real keys).</summary>
public static partial class AppConfiguration
{
    /// <summary>Reputation API key; empty disables online link checks. Never ship a real key in the app (use a server proxy).</summary>
    public static string? SafeBrowsingApiKey => null;

    /// <summary>
    /// GitHub-release update checks are only for sideloaded builds. Store policies (Google Play, App Store,
    /// Microsoft Store) forbid self-updating, so store builds compile with -p:DefineConstants=STORE_BUILD.
    /// </summary>
    public static bool UpdateChecksEnabled =>
#if STORE_BUILD
        false;
#else
        DeviceInfo.Current.Platform == DevicePlatform.WinUI || DeviceInfo.Current.Platform == DevicePlatform.Android;
#endif
}
