namespace QuickQr.Services;

/// <summary>Text or an image another app shared with QuickQr (Android share sheet), waiting to be scanned.</summary>
public sealed record SharedContent(string? Text, byte[]? ImageBytes);

/// <summary>
/// Hand-off between the platform entry point (MainActivity) and the scan screen. Holds the latest item until the
/// scan screen takes it, so a cold start from the share sheet works the same as a warm one.
/// </summary>
public interface ISharedContentInbox
{
    event EventHandler? ContentAvailable;
    bool HasContent { get; }
    void Submit(SharedContent content);
    SharedContent? Take();
}

public sealed class SharedContentInbox : ISharedContentInbox
{
    private readonly Lock _gate = new();
    private SharedContent? _pending;

    public event EventHandler? ContentAvailable;

    public bool HasContent
    {
        get { lock (_gate) return _pending is not null; }
    }

    public void Submit(SharedContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_gate) _pending = content;
        ContentAvailable?.Invoke(this, EventArgs.Empty);
    }

    public SharedContent? Take()
    {
        lock (_gate)
        {
            var item = _pending;
            _pending = null;
            return item;
        }
    }
}
