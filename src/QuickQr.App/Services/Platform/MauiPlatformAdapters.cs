using QuickQr.Core.Platform;

namespace QuickQr.Services.Platform;

/// <summary>IKeyValueStore over MAUI Preferences.</summary>
public sealed class PreferencesKeyValueStore : IKeyValueStore
{
    public string? Get(string key) => Preferences.Default.ContainsKey(key) ? Preferences.Default.Get<string?>(key, null) : null;

    public void Set(string key, string? value)
    {
        if (value is null) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
    }

    public void Remove(string key) => Preferences.Default.Remove(key);
}

/// <summary>ISecureKeyValueStore over MAUI SecureStorage (Keychain / Keystore / DPAPI) with a Preferences fallback
/// for platforms where secure storage is unavailable (e.g. unsigned Mac Catalyst debug builds).</summary>
public sealed class SecureStorageKeyValueStore : ISecureKeyValueStore
{
    private const string FallbackPrefix = "securefallback.";

    public async Task<string?> GetAsync(string key)
    {
        try { return await SecureStorage.Default.GetAsync(key); }
        catch (Exception) { return Preferences.Default.Get<string?>(FallbackPrefix + key, null); }
    }

    public async Task SetAsync(string key, string value)
    {
        try { await SecureStorage.Default.SetAsync(key, value); }
        catch (Exception) { Preferences.Default.Set(FallbackPrefix + key, value); }
    }

    public Task RemoveAsync(string key)
    {
        try { SecureStorage.Default.Remove(key); }
        catch (Exception) { /* fall through to fallback removal */ }
        Preferences.Default.Remove(FallbackPrefix + key);
        return Task.CompletedTask;
    }
}

public sealed class MauiAppInfoProvider : IAppInfoProvider
{
    public string AppName => AppInfo.Current.Name;
    public string Version => AppInfo.Current.VersionString;
    public string Build => AppInfo.Current.BuildString;
    public string Platform => DeviceInfo.Current.Platform.ToString();
    public string AppDataDirectory => FileSystem.Current.AppDataDirectory;
    public string CacheDirectory => FileSystem.Current.CacheDirectory;
}

public sealed class MauiNetworkStatus : INetworkStatus
{
    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
