using Microsoft.Extensions.Logging;
#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using System.Runtime.Versioning;
#elif IOS || MACCATALYST
using LocalAuthentication;
#elif WINDOWS
using Windows.Security.Credentials.UI;
#endif

namespace QuickQr.Services;

/// <summary>
/// Device-owner authentication with the platform's own APIs (no third-party packages):
/// Android <c>BiometricPrompt</c> with device-credential fallback (API 29+) or the keyguard PIN screen (API 24–28),
/// iOS/Mac <c>LAContext</c> (Face ID / Touch ID / passcode) and Windows Hello. Devices without a secure lock screen
/// report "unavailable" so the app lock can't be enabled there.
/// </summary>
public sealed class AppLockService(ILogger<AppLockService> logger) : IAppLockService
{
    private const string PromptTitle = "Unlock QuickQr";

    public Task<bool> IsAvailableAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return await IsAvailableCoreAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "App lock availability check failed");
                return false;
            }
        });

    public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return await AuthenticateCoreAsync(string.IsNullOrWhiteSpace(reason) ? PromptTitle : reason, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Authentication failed");
                return false;
            }
        });

#if ANDROID
    private static Task<bool> IsAvailableCoreAsync()
    {
        var context = Android.App.Application.Context;
        // Every path below accepts the device PIN/pattern/password, so a secure lock screen is all that's needed.
        return Task.FromResult(context.GetSystemService(Context.KeyguardService) is KeyguardManager { IsDeviceSecure: true });
    }

    private static async Task<bool> AuthenticateCoreAsync(string reason, CancellationToken cancellationToken)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("No foreground activity.");

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return await BiometricPromptAsync(activity, reason, cancellationToken);

        // Android 7–9: the system's confirm-credential screen (PIN, pattern, password or fingerprint).
        if (activity.GetSystemService(Context.KeyguardService) is not KeyguardManager keyguard)
            return false;

        var intent = CreateConfirmCredentialIntent(keyguard, reason);
        if (intent is null) return false; // No secure lock screen.

        var result = await ActivityResultBridge.StartAsync(activity, intent);
        return result == Result.Ok;
    }

    private static Intent? CreateConfirmCredentialIntent(KeyguardManager keyguard, string reason)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
        return keyguard.CreateConfirmDeviceCredentialIntent(PromptTitle, reason);
    }

    [SupportedOSPlatform("android29.0")]
    private static Task<bool> BiometricPromptAsync(Activity activity, string reason, CancellationToken cancellationToken)
    {
        var builder = new BiometricPrompt.Builder(activity)
            .SetTitle(PromptTitle)
            .SetSubtitle(reason);

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            builder.SetAllowedAuthenticators((int)(BiometricManagerAuthenticators.BiometricStrong | BiometricManagerAuthenticators.DeviceCredential));
        else
            builder.SetDeviceCredentialAllowed(true);

        var prompt = builder.Build();
        var callback = new PromptCallback();
        var cancellation = new Android.OS.CancellationSignal();
        var registration = cancellationToken.Register(() => cancellation.Cancel());
        prompt.Authenticate(cancellation, activity.MainExecutor!, callback);

        return callback.Result.ContinueWith(t =>
        {
            registration.Dispose();
            return t.Result;
        }, TaskScheduler.Default);
    }

    [SupportedOSPlatform("android29.0")]
    private sealed class PromptCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> Result => _completion.Task;

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
            _completion.TrySetResult(true);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            _completion.TrySetResult(false);

        // OnAuthenticationFailed = one rejected attempt; the prompt stays open for another try.
    }
#elif IOS || MACCATALYST
    private static Task<bool> IsAvailableCoreAsync()
    {
        using var context = new LAContext();
        return Task.FromResult(context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _));
    }

    private static async Task<bool> AuthenticateCoreAsync(string reason, CancellationToken cancellationToken)
    {
        using var context = new LAContext();
        if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _))
            return false;

        using var registration = cancellationToken.Register(context.Invalidate);
        var result = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason);
        return result.Item1;
    }
#elif WINDOWS
    private static async Task<bool> IsAvailableCoreAsync() =>
        await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;

    private static async Task<bool> AuthenticateCoreAsync(string reason, CancellationToken cancellationToken)
    {
        if (await UserConsentVerifier.CheckAvailabilityAsync() != UserConsentVerifierAvailability.Available)
            return false;

        // Desktop apps must parent the Windows Hello dialog to their window.
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        var result = window is null
            ? await UserConsentVerifier.RequestVerificationAsync(reason).AsTask(cancellationToken)
            : await UserConsentVerifierInterop.RequestVerificationForWindowAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), reason).AsTask(cancellationToken);
        return result == UserConsentVerificationResult.Verified;
    }
#else
    private static Task<bool> IsAvailableCoreAsync() => Task.FromResult(false);

    private static Task<bool> AuthenticateCoreAsync(string reason, CancellationToken cancellationToken) => Task.FromResult(false);
#endif
}
