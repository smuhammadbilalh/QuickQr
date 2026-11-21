using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core.Platform;
using QuickQr.Core.Serialization;
using QuickQr.Core.Settings;

namespace QuickQr.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON in <see cref="IKeyValueStore"/> (<c>settings.v1</c>).
/// Loaded lazily on first access; unreadable data falls back to defaults. Thread-safe.
/// </summary>
public sealed class AppSettingsService(IKeyValueStore store, ILogger<AppSettingsService>? logger = null) : IAppSettingsService
{
    internal const string StorageKey = "settings.v1";

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    private readonly Lock _gate = new();
    private AppSettings? _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current ??= Load();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            store.Set(StorageKey, JsonSerializer.Serialize(settings, CoreJsonContext.Default.AppSettings));
            _current = settings;
        }

        SettingsChanged?.Invoke(this, settings);
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaults = new AppSettings();

        lock (_gate)
        {
            store.Remove(StorageKey);
            _current = defaults;
        }

        SettingsChanged?.Invoke(this, defaults);
        return Task.CompletedTask;
    }

    private AppSettings Load()
    {
        var json = store.Get(StorageKey);
        if (string.IsNullOrWhiteSpace(json)) return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stored settings are unreadable; using defaults.");
            return new AppSettings();
        }
    }
}
