namespace QuickQr.Core.Platform;

/// <summary>Plain key/value persistence (MAUI Preferences on device, in-memory in tests).</summary>
public interface IKeyValueStore
{
    string? Get(string key);
    void Set(string key, string? value);
    void Remove(string key);
}

/// <summary>Encrypted key/value persistence (Keychain / Android Keystore / DPAPI).</summary>
public interface ISecureKeyValueStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}

public interface IAppInfoProvider
{
    string AppName { get; }
    /// <summary>Semantic version, e.g. "3.0.0".</summary>
    string Version { get; }
    string Build { get; }
    /// <summary>"Android", "iOS", "MacCatalyst", "WinUI".</summary>
    string Platform { get; }
    /// <summary>Directory for app data (databases, caches that must survive).</summary>
    string AppDataDirectory { get; }
    string CacheDirectory { get; }
}

public interface INetworkStatus
{
    bool IsOnline { get; }
}

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string? Get(string key)
    {
        lock (_gate) return _values.GetValueOrDefault(key);
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;
        }
    }

    public void Remove(string key)
    {
        lock (_gate) _values.Remove(key);
    }
}
