using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.Logging;
using QuickQr.Core;
#if ANDROID
using Android.Content;
using Android.Provider;
using AndroidEnvironment = Android.OS.Environment;
#elif IOS || MACCATALYST
using Foundation;
using Photos;
#endif

namespace QuickQr.Services;

/// <summary>
/// Saves images to the platform gallery: Android MediaStore (Pictures/QuickQr, scoped storage on API 29+),
/// the iOS/Mac Photos library (add-only access) and Pictures\QuickQr on Windows.
/// </summary>
public sealed class FileExportService(ILogger<FileExportService> logger) : IFileExportService
{
    internal const string AlbumName = "QuickQr";
    private const string TempFolderName = "exports";
    private static readonly TimeSpan TempFileLifetime = TimeSpan.FromDays(1);
    private int _tempCleaned;

    public async Task<Result<string>> SaveImageToGalleryAsync(byte[] imageBytes, string fileName, string mimeType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        fileName = SanitizeFileName(fileName);

        try
        {
#if ANDROID
            return await SaveToMediaStoreAsync(imageBytes, fileName, mimeType, cancellationToken);
#elif IOS || MACCATALYST
            return await SaveToPhotosAsync(imageBytes, fileName);
#elif WINDOWS
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AlbumName);
            Directory.CreateDirectory(folder);
            var path = UniquePath(folder, fileName);
            await File.WriteAllBytesAsync(path, imageBytes, cancellationToken);
            return path;
#else
            await Task.CompletedTask;
            return Error.Unexpected("Saving images is not supported on this platform.");
#endif
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogInformation(ex, "Gallery access denied");
            return Error.Validation(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving {FileName} to the gallery failed", fileName);
            return Error.Unexpected("The image could not be saved. " + ex.Message);
        }
    }

    public async Task<Result<string>> SaveFileAsync(Stream data, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            var result = await MainThread.InvokeOnMainThreadAsync(() =>
                FileSaver.Default.SaveAsync(SanitizeFileName(fileName), data, cancellationToken));

            if (result.IsSuccessful && result.FilePath is { } path) return path;
            if (result.IsCancelled) return new Error(ErrorCodes.Cancelled, "Saving was cancelled.");
            return Error.Unexpected(result.Exception?.Message ?? "The file could not be saved.");
        }
        catch (OperationCanceledException)
        {
            return new Error(ErrorCodes.Cancelled, "Saving was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving {FileName} failed", fileName);
            return Error.Unexpected("The file could not be saved. " + ex.Message);
        }
    }

    public async Task<string> WriteTempFileAsync(byte[] data, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var folder = Path.Combine(FileSystem.Current.CacheDirectory, TempFolderName);
        Directory.CreateDirectory(folder);

        if (Interlocked.Exchange(ref _tempCleaned, 1) == 0)
            CleanOldTempFiles(folder);

        // A per-call sub folder keeps the user-visible name intact when the same name is shared twice.
        var callFolder = Path.Combine(folder, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(callFolder);
        var path = Path.Combine(callFolder, SanitizeFileName(fileName));
        await File.WriteAllBytesAsync(path, data, cancellationToken);
        return path;
    }

    internal static string SanitizeFileName(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "quickqr" : fileName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c).ToArray());
        return name.Length > 120 ? name[..120] : name;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){extension}");
        return path;
    }

    private void CleanOldTempFiles(string folder)
    {
        try
        {
            var cutoff = DateTime.UtcNow - TempFileLifetime;
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                    Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Temp file clean-up failed");
        }
    }

#if ANDROID
    private static async Task<Result<string>> SaveToMediaStoreAsync(byte[] bytes, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        var context = Android.App.Application.Context;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var resolver = context.ContentResolver ?? throw new IOException("The media store is unavailable.");
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
            values.Put(MediaStore.IMediaColumns.RelativePath, $"{AndroidEnvironment.DirectoryPictures}/{AlbumName}");
            values.Put(MediaStore.IMediaColumns.IsPending, 1);

            var collection = MediaStore.Images.Media.ExternalContentUri ?? throw new IOException("The media store is unavailable.");
            var uri = resolver.Insert(collection, values) ?? throw new IOException("The gallery entry could not be created.");
            try
            {
                await using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("The gallery entry could not be opened."))
                {
                    await output.WriteAsync(bytes, cancellationToken);
                }

                values.Clear();
                values.Put(MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
                return $"{AndroidEnvironment.DirectoryPictures}/{AlbumName}/{fileName}";
            }
            catch
            {
                resolver.Delete(uri, null, null);
                throw;
            }
        }

        // Android 9 and below: classic public storage (WRITE_EXTERNAL_STORAGE, maxSdkVersion 28).
        var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.StorageWrite>());
        if (status != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("Allow storage access to save images to your gallery.");

        var pictures = AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryPictures)?.AbsolutePath
            ?? throw new IOException("The Pictures folder is unavailable.");
        var folder = Path.Combine(pictures, AlbumName);
        Directory.CreateDirectory(folder);
        var path = UniquePath(folder, fileName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        Android.Media.MediaScannerConnection.ScanFile(context, [path], [mimeType], null);
        return path;
    }
#endif

#if IOS || MACCATALYST
    private static async Task<Result<string>> SaveToPhotosAsync(byte[] bytes, string fileName)
    {
        var status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.AddOnly);
        if (status is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
            throw new UnauthorizedAccessException("Allow QuickQr to add photos in Settings to save images.");

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () =>
            {
                var request = PHAssetCreationRequest.CreationRequestForAsset();
                request.AddResource(PHAssetResourceType.Photo, NSData.FromArray(bytes), new PHAssetResourceCreationOptions
                {
                    OriginalFilename = fileName,
                });
            },
            (success, error) =>
            {
                if (success) completion.TrySetResult();
                else completion.TrySetException(new IOException(error?.LocalizedDescription ?? "Photos refused the image."));
            });

        await completion.Task;
        return "Photos";
    }
#endif
}
