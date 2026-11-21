using Microsoft.Extensions.Logging;
#if ANDROID
using Android.Content.PM;
#elif IOS || MACCATALYST
using AVFoundation;
#elif WINDOWS
using Windows.Devices.Enumeration;
#endif

namespace QuickQr.Services;

/// <summary>Camera presence and permission for the live scanner.</summary>
public interface ICameraService
{
    /// <summary>False on devices without any camera (e.g. a desktop without a webcam).</summary>
    Task<bool> IsCameraAvailableAsync();

    /// <summary>Asks for camera access when needed. True when the scanner may start.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>Opens QuickQr's page in the system settings (to re-enable a denied permission).</summary>
    void OpenAppSettings();
}

public sealed class CameraService(ILogger<CameraService> logger) : ICameraService
{
    private bool? _available;

    public async Task<bool> IsCameraAvailableAsync()
    {
        if (_available is { } cached) return cached;

        try
        {
            _available = await DetectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera detection failed; assuming a camera is present");
            return true;
        }

        return _available.Value;
    }

    public Task<bool> RequestPermissionAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return await BarcodeScanning.Methods.AskForRequiredPermissionAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Camera permission request failed");
                return false;
            }
        });

    public void OpenAppSettings()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(AppInfo.Current.ShowSettingsUI);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open the app settings");
        }
    }

#if ANDROID
    private static Task<bool> DetectAsync() =>
        Task.FromResult(Android.App.Application.Context.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) ?? false);
#elif IOS || MACCATALYST
    private static Task<bool> DetectAsync() =>
        Task.FromResult(AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is not null);
#elif WINDOWS
    private static async Task<bool> DetectAsync() =>
        (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)).Count > 0;
#else
    private static Task<bool> DetectAsync() => Task.FromResult(false);
#endif
}
