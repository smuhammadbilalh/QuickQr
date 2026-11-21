namespace QuickQr.Services;

/// <summary>
/// Photo picking uses the system photo picker on phones/tablets (no storage permission needed on Android 13+)
/// and the file picker on desktop, where "photos" are just image files.
/// </summary>
public sealed class MediaPickerService : IMediaPickerService
{
    public Task<FileResult?> PickImageAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
            {
                return await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Choose an image",
                    FileTypes = FilePickerFileType.Images,
                });
            }

            var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Choose an image",
                SelectionLimit = 1,
            });
            return results?.FirstOrDefault();
        });

    public Task<FileResult?> PickFileAsync(IReadOnlyList<string> extensions) =>
        MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a file",
            FileTypes = extensions.Count == 0 ? null : FileTypesFor(extensions),
        }));

    /// <summary>Maps extensions (".csv", "txt") to each platform's vocabulary: MIME types, UTTypes or extensions.</summary>
    internal static FilePickerFileType FileTypesFor(IReadOnlyList<string> extensions)
    {
        var normalized = extensions
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();

        return new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = normalized.Select(MimeTypeFor).Distinct().ToList(),
            [DevicePlatform.iOS] = normalized.Select(UniformTypeFor).Distinct().ToList(),
            [DevicePlatform.MacCatalyst] = normalized.Select(UniformTypeFor).Distinct().ToList(),
            [DevicePlatform.WinUI] = normalized.Select(e => "." + e).ToList(),
        });
    }

    private static string MimeTypeFor(string extension) => extension switch
    {
        "csv" => "text/comma-separated-values",
        "tsv" => "text/tab-separated-values",
        "txt" => "text/plain",
        "json" => "application/json",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "pdf" => "application/pdf",
        "zip" => "application/zip",
        _ => "*/*",
    };

    private static string UniformTypeFor(string extension) => extension switch
    {
        "csv" => "public.comma-separated-values-text",
        "tsv" => "public.tab-separated-values-text",
        "txt" => "public.plain-text",
        "json" => "public.json",
        "xlsx" => "org.openxmlformats.spreadsheetml.sheet",
        "png" => "public.png",
        "jpg" or "jpeg" => "public.jpeg",
        "webp" => "org.webmproject.webp",
        "svg" => "public.svg-image",
        "pdf" => "com.adobe.pdf",
        "zip" => "public.zip-archive",
        _ => "public.data",
    };
}
