using Microsoft.Extensions.Logging;

namespace QuickQr.Services;

/// <summary>Opens links, apps and files outside QuickQr (keeps MAUI statics out of view models).</summary>
public interface IExternalLauncher
{
    /// <summary>Opens an http(s) link in the system browser, or any other URI with the app that handles it.</summary>
    Task<bool> OpenUrlAsync(string url);

    /// <summary>Opens <paramref name="uri"/> when an app handles it. False when nothing can (no exception).</summary>
    Task<bool> TryOpenAsync(string uri);

    /// <summary>Opens a local file with the default app (contacts for .vcf, calendar for .ics, ...).</summary>
    Task<bool> OpenFileAsync(string path, string contentType, string title);
}

public sealed class ExternalLauncher(ILogger<ExternalLauncher> logger) : IExternalLauncher
{
    public Task<bool> OpenUrlAsync(string url) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

            try
            {
                if (uri.Scheme is "http" or "https")
                    return await Browser.Default.OpenAsync(uri, BrowserLaunchMode.External);

                return await Launcher.Default.TryOpenAsync(uri);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not open {Scheme} link", uri.Scheme);
                return false;
            }
        });

    public Task<bool> TryOpenAsync(string uri) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
            try
            {
                return await Launcher.Default.TryOpenAsync(parsed);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No handler for {Scheme}", parsed.Scheme);
                return false;
            }
        });

    public Task<bool> OpenFileAsync(string path, string contentType, string title) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return await Launcher.Default.OpenAsync(new OpenFileRequest(title, new ReadOnlyFile(path, contentType)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not open {ContentType} file", contentType);
                return false;
            }
        });
}
