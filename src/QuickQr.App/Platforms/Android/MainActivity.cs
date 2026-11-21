using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.Logging;
using QuickQr.Services;

namespace QuickQr
{
    /// <summary>
    /// Main activity. Also a share target: text or an image shared from another app ("Scan with QuickQr") is handed
    /// to <see cref="ISharedContentInbox"/> and the Scan tab reads it.
    /// </summary>
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain", Label = "Scan with QuickQr")]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*", Label = "Scan with QuickQr")]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int MaxSharedImageBytes = 25 * 1024 * 1024;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Read the share intent before the window starts so the start-up hook already sees it.
            if (savedInstanceState is null)
                HandleShareIntent(Intent);

            base.OnCreate(savedInstanceState);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (HandleShareIntent(intent))
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (Shell.Current is { } shell)
                            await shell.GoToAsync("//" + Routes.Scan);
                    }
                    catch (Exception ex)
                    {
                        Log(ex, "Opening the scan tab failed");
                    }
                });
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            ActivityResultBridge.OnActivityResult(requestCode, resultCode);
        }

        private bool HandleShareIntent(Intent? intent)
        {
            if (intent?.Action != Intent.ActionSend) return false;

            try
            {
                SharedContent? shared = null;
                var type = intent.Type ?? string.Empty;
                if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = ReadSharedImage(intent);
                    if (bytes is { Length: > 0 }) shared = new SharedContent(null, bytes);
                }
                else
                {
                    var text = intent.GetStringExtra(Intent.ExtraText);
                    if (!string.IsNullOrWhiteSpace(text)) shared = new SharedContent(text, null);
                }

                // Consume the intent so rotation or a later resume doesn't process it twice.
                intent.SetAction(Intent.ActionMain);
                if (shared is null) return false;

                var inbox = IPlatformApplication.Current?.Services.GetService<ISharedContentInbox>();
                inbox?.Submit(shared);
                return inbox is not null;
            }
            catch (Exception ex)
            {
                Log(ex, "Reading shared content failed");
                return false;
            }
        }

        private byte[]? ReadSharedImage(Intent intent)
        {
            Android.Net.Uri? uri;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                uri = intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri))) as Android.Net.Uri;
            else
                uri = GetLegacyStreamExtra(intent);

            uri ??= intent.ClipData is { ItemCount: > 0 } clip ? clip.GetItemAt(0)?.Uri : null;
            if (uri is null || ContentResolver is null) return null;

            using var input = ContentResolver.OpenInputStream(uri);
            if (input is null) return null;

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxSharedImageBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        private static Android.Net.Uri? GetLegacyStreamExtra(Intent intent)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) return null;
            return intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
        }

        private static void Log(Exception ex, string message) =>
            IPlatformApplication.Current?.Services.GetService<ILogger<MainActivity>>()?.LogWarning(ex, message);
    }
}
