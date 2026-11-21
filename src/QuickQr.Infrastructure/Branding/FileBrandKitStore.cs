using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;
using QuickQr.Core.Rendering;
using QuickQr.Core.Serialization;

namespace QuickQr.Infrastructure.Branding;

/// <summary>
/// Stores brand kits in <c>{AppDataDirectory}/brandkits.json</c> (atomic temp-file + move writes).
/// Saving requires <see cref="PremiumFeature.BrandKits"/>; reading and deleting are always allowed.
/// </summary>
public sealed class FileBrandKitStore(
    IAppInfoProvider appInfo,
    IEntitlementService entitlements,
    ILogger<FileBrandKitStore>? logger = null) : IBrandKitStore
{
    internal const string FileName = "brandkits.json";
    internal const int MaxKits = 50;
    internal const int MaxNameLength = 60;

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<BrandKit>? _kits;

    internal string FilePath => Path.Combine(appInfo.AppDataDirectory, FileName);

    public async Task<IReadOnlyList<BrandKit>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return [.. await LoadAsync(cancellationToken).ConfigureAwait(false)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> SaveAsync(BrandKit kit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kit);
        if (!entitlements.HasFeature(PremiumFeature.BrandKits))
            return Result.Failure(Error.FeatureLocked($"Upgrade to use: {FeatureCatalog.Get(PremiumFeature.BrandKits).Name}."));

        var name = kit.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Result.Failure(Error.Validation("Give the brand kit a name."));
        if (name.Length > MaxNameLength)
            return Result.Failure(Error.Validation($"Brand kit names can be up to {MaxNameLength} characters."));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var kits = new List<BrandKit>(await LoadAsync(cancellationToken).ConfigureAwait(false));
            var index = kits.FindIndex(k => k.Id == kit.Id);
            var normalized = kit with { Name = name };
            if (index >= 0)
            {
                kits[index] = normalized;
            }
            else
            {
                if (kits.Count >= MaxKits)
                    return Result.Failure(Error.Validation($"You can save up to {MaxKits} brand kits. Delete one to add another."));
                kits.Add(normalized);
            }

            await WriteAsync(kits, cancellationToken).ConfigureAwait(false);
            _kits = kits;
            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var kits = new List<BrandKit>(await LoadAsync(cancellationToken).ConfigureAwait(false));
            if (kits.RemoveAll(k => k.Id == id) == 0) return;
            await WriteAsync(kits, cancellationToken).ConfigureAwait(false);
            _kits = kits;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<BrandKit>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_kits is not null) return _kits;

        var path = FilePath;
        if (!File.Exists(path)) return _kits = [];

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                _kits = await JsonSerializer.DeserializeAsync(stream, CoreJsonContext.Default.ListBrandKit, cancellationToken).ConfigureAwait(false) ?? [];
            }
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for support instead of overwriting it on the next save.
            _logger.LogWarning(ex, "Brand kit file is corrupt; starting with an empty list.");
            TryMove(path, path + ".corrupt");
            _kits = [];
        }

        return _kits;
    }

    private async Task WriteAsync(List<BrandKit> kits, CancellationToken cancellationToken)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";

        var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 16 * 1024, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, kits, CoreJsonContext.Default.ListBrandKit, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    private void TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not set aside the corrupt brand kit file.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not set aside the corrupt brand kit file.");
        }
    }
}
