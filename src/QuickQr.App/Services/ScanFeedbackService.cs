using Microsoft.Extensions.Logging;
using QuickQr.Core.Settings;
#if ANDROID
using Android.Media;
#elif IOS || MACCATALYST
using AudioToolbox;
#endif

namespace QuickQr.Services;

/// <summary>Vibration and a short beep after a successful scan, as configured in settings.</summary>
public sealed class ScanFeedbackService(IAppSettingsService settings, ILogger<ScanFeedbackService> logger) : IScanFeedbackService
{
    private static readonly TimeSpan VibrationLength = TimeSpan.FromMilliseconds(60);

    public void OnScanSucceeded()
    {
        var current = settings.Current;
        if (current.VibrateOnScan) Vibrate();
        if (current.BeepOnScan) Beep();
    }

    private void Vibrate()
    {
        try
        {
            if (Vibration.Default.IsSupported)
                Vibration.Default.Vibrate(VibrationLength);
            else if (HapticFeedback.Default.IsSupported)
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Scan vibration unavailable");
        }
    }

    private void Beep()
    {
        try
        {
#if ANDROID
            var tone = new ToneGenerator(Android.Media.Stream.Notification, 80);
            tone.StartTone(Tone.PropBeep, 120);
            _ = Task.Delay(300).ContinueWith(_ => tone.Release(), TaskScheduler.Default);
#elif IOS || MACCATALYST
            // 1057 = "Tink", the short system sound used by many scanners.
            new SystemSound(1057).PlaySystemSound();
#endif
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Scan beep unavailable");
        }
    }
}
