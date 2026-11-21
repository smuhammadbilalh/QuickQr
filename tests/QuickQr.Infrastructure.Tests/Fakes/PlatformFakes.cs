using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using QuickQr.Core.Platform;

namespace QuickQr.Infrastructure.Tests.Fakes;

public sealed class FakeAppInfoProvider(string appDataDirectory, string version = "3.0.0") : IAppInfoProvider
{
    public string AppName => "QuickQr";
    public string Version { get; set; } = version;
    public string Build => "3";
    public string Platform => "Tests";
    public string AppDataDirectory { get; } = appDataDirectory;
    public string CacheDirectory => Path.Combine(AppDataDirectory, "cache");
}

public sealed class FakeNetworkStatus : INetworkStatus
{
    public bool IsOnline { get; set; } = true;
}

public sealed class InMemorySecureKeyValueStore : ISecureKeyValueStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool ThrowOnAccess { get; set; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public Task<string?> GetAsync(string key)
    {
        if (ThrowOnAccess) throw new InvalidOperationException("Keystore unavailable.");
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value)
    {
        if (ThrowOnAccess) throw new InvalidOperationException("Keystore unavailable.");
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>A unique temporary directory, deleted (with SQLite pools cleared) on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickqr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}
