namespace QuickQr.Infrastructure;

/// <summary>Configuration for infrastructure adapters. Bound in MauiProgram.</summary>
public sealed class QuickQrInfrastructureOptions
{
    /// <summary>Full path of the SQLite database. Defaults to {AppDataDirectory}/quickqr.db3.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>GitHub repository used for update checks ("owner/name").</summary>
    public string GitHubRepository { get; set; } = "smuhammadbilalh/QuickQr";

    /// <summary>Minimum time between automatic update checks.</summary>
    public TimeSpan UpdateCheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Google Safe Browsing v4 API key. Empty disables online link reputation checks.</summary>
    public string? SafeBrowsingApiKey { get; set; }

    /// <summary>Use the simulated store (debug builds, CI, unsupported platforms).</summary>
    public bool UseSimulatedStore { get; set; }
}
