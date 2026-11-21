namespace QuickQr.Core.Updates;

public enum UpdateStatus { UpToDate = 0, UpdateAvailable = 1, Ahead = 2, Throttled = 3, Offline = 4, Failed = 5 }

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseNotes = null,
    Uri? DownloadUrl = null,
    DateTimeOffset? PublishedAt = null,
    string? Error = null);

/// <summary>Checks the GitHub releases feed for a newer version.</summary>
public interface IUpdateService
{
    /// <param name="force">Ignore the throttle window (manual "Check for updates").</param>
    Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default);
}
