using Microsoft.Extensions.Logging;
using QuickQr.Core.Content;
#if ANDROID
using Android.Content;
using Android.Net.Wifi;
using Android.OS;
using AndroidSettings = Android.Provider.Settings;
#elif IOS
using Foundation;
using NetworkExtension;
#endif

namespace QuickQr.Services.Actions;

public enum WifiConnectOutcome
{
    /// <summary>The system joined (or is joining) the network.</summary>
    Connected,
    /// <summary>The system showed its own "add network" confirmation.</summary>
    Prompted,
    /// <summary>The network was suggested; the system connects when it is in range.</summary>
    Suggested,
    /// <summary>Automatic connection is unavailable; the Wi-Fi settings were opened instead.</summary>
    OpenedSettings,
    /// <summary>Automatic connection is unavailable and no settings screen could be opened.</summary>
    Unsupported,
    Cancelled,
    Failed,
}

public interface IWifiConnector
{
    Task<WifiConnectOutcome> ConnectAsync(WifiContent wifi, CancellationToken cancellationToken = default);
}

/// <summary>
/// Android 11+: system "add network" sheet (<c>ACTION_WIFI_ADD_NETWORKS</c>); Android 10: network suggestion;
/// older Android: Wi-Fi settings. iOS: <c>NEHotspotConfiguration</c>. Windows: Wi-Fi settings.
/// Callers copy the password first whenever the result is not <see cref="WifiConnectOutcome.Connected"/>.
/// </summary>
public sealed class WifiConnector(ILogger<WifiConnector> logger) : IWifiConnector
{
    public Task<WifiConnectOutcome> ConnectAsync(WifiContent wifi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wifi);
        return MainThread.InvokeOnMainThreadAsync(() => ConnectOnMainThreadAsync(wifi));
    }

#if ANDROID
    private Task<WifiConnectOutcome> ConnectOnMainThreadAsync(WifiContent wifi)
    {
        var context = (Context?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? Android.App.Application.Context;
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30) && BuildSuggestion(wifi) is { } suggestion)
            {
                var intent = new Intent(AndroidSettings.ActionWifiAddNetworks);
                intent.PutParcelableArrayListExtra(AndroidSettings.ExtraWifiNetworkList, new List<IParcelable> { suggestion });
                Start(context, intent);
                return Task.FromResult(WifiConnectOutcome.Prompted);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(29) && BuildSuggestion(wifi) is { } legacySuggestion &&
                context.GetSystemService(Context.WifiService) is WifiManager manager)
            {
                var status = manager.AddNetworkSuggestions(new List<WifiNetworkSuggestion> { legacySuggestion });
                if ((int)status == 0)
                {
                    Start(context, new Intent(AndroidSettings.Panel.ActionWifi));
                    return Task.FromResult(WifiConnectOutcome.Suggested);
                }

                logger.LogInformation("Wi-Fi suggestion rejected with status {Status}", status);
            }

            Start(context, new Intent(AndroidSettings.ActionWifiSettings));
            return Task.FromResult(WifiConnectOutcome.OpenedSettings);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Wi-Fi connection request failed");
            return Task.FromResult(WifiConnectOutcome.Failed);
        }
    }

    private static void Start(Context context, Intent intent)
    {
        if (context is not Android.App.Activity)
            intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private static WifiNetworkSuggestion? BuildSuggestion(WifiContent wifi)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;

        var builder = new WifiNetworkSuggestion.Builder().SetSsid(wifi.Ssid);
        switch (wifi.Security)
        {
            case WifiSecurity.None:
                break;
            case WifiSecurity.Wpa when !string.IsNullOrEmpty(wifi.Password):
                builder.SetWpa2Passphrase(wifi.Password);
                break;
            case WifiSecurity.Wpa3 when !string.IsNullOrEmpty(wifi.Password):
                builder.SetWpa3Passphrase(wifi.Password);
                break;
            default:
                // WEP (and secured networks without a password) cannot be suggested.
                return null;
        }

        if (wifi.Hidden) builder.SetIsHiddenSsid(true);
        return builder.Build();
    }
#elif IOS
    private async Task<WifiConnectOutcome> ConnectOnMainThreadAsync(WifiContent wifi)
    {
        try
        {
            var configuration = wifi.Security == WifiSecurity.None || string.IsNullOrEmpty(wifi.Password)
                ? new NEHotspotConfiguration(wifi.Ssid)
                : new NEHotspotConfiguration(wifi.Ssid, wifi.Password, wifi.Security == WifiSecurity.Wep);
            configuration.JoinOnce = false;
            configuration.Hidden = wifi.Hidden;

            await NEHotspotConfigurationManager.SharedManager.ApplyConfigurationAsync(configuration);
            return WifiConnectOutcome.Connected;
        }
        catch (NSErrorException ex) when (ex.Error.Domain == "NEHotspotConfigurationErrorDomain")
        {
            // 7 = userDenied, 13 = alreadyAssociated (see NEHotspotConfigurationError).
            return ex.Error.Code switch
            {
                13 => WifiConnectOutcome.Connected,
                7 => WifiConnectOutcome.Cancelled,
                _ => Fail(ex),
            };
        }
        catch (Exception ex)
        {
            // Missing "Hotspot Configuration" entitlement, simulator, ...: fall back to manual joining.
            return Fail(ex);
        }
    }

    private WifiConnectOutcome Fail(Exception ex)
    {
        logger.LogWarning(ex, "NEHotspotConfiguration failed");
        return WifiConnectOutcome.Unsupported;
    }
#elif WINDOWS
    private async Task<WifiConnectOutcome> ConnectOnMainThreadAsync(WifiContent wifi)
    {
        try
        {
            return await Launcher.Default.TryOpenAsync(new Uri("ms-settings:network-wifi"))
                ? WifiConnectOutcome.OpenedSettings
                : WifiConnectOutcome.Unsupported;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Opening Wi-Fi settings for {Ssid} failed", wifi.Ssid);
            return WifiConnectOutcome.Unsupported;
        }
    }
#else
    private Task<WifiConnectOutcome> ConnectOnMainThreadAsync(WifiContent wifi)
    {
        logger.LogDebug("Automatic Wi-Fi joining is not available for {Ssid}", wifi.Ssid);
        return Task.FromResult(WifiConnectOutcome.Unsupported);
    }
#endif
}
