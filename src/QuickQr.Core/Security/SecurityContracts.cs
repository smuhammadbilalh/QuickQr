namespace QuickQr.Core.Security;

public enum RiskLevel { Safe = 0, Low = 1, Medium = 2, High = 3 }

/// <param name="Code">Stable identifier, e.g. "insecure_scheme", "punycode_host", "ip_literal_host", "url_shortener".</param>
public sealed record SafetySignal(string Code, string Description, RiskLevel Severity);

/// <param name="DisplayHost">The host as the user should see it (Unicode decoded, without credentials).</param>
public sealed record UrlSafetyReport(
    string Url,
    RiskLevel Level,
    IReadOnlyList<SafetySignal> Signals,
    string? DisplayHost)
{
    public bool IsSafe => Level == RiskLevel.Safe;
}

/// <summary>Offline heuristic URL analysis to defend against QR phishing ("quishing"). Runs on every scanned URL.</summary>
public interface IUrlSafetyAnalyzer
{
    UrlSafetyReport Analyze(string url);
}

public enum ReputationVerdict { Unknown = 0, Clean = 1, Malicious = 2, NotConfigured = 3 }

public sealed record ReputationResult(ReputationVerdict Verdict, IReadOnlyList<string> Threats, string Provider);

/// <summary>Online reputation lookup (e.g. Google Safe Browsing). Premium feature; returns NotConfigured without an API key.</summary>
public interface IUrlReputationService
{
    Task<ReputationResult> CheckAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Passphrase-based encryption of QR payloads ("Secure QR").
/// Envelope: "QQSEC1:" + base64url(version | salt | iv | ciphertext | hmac).
/// Algorithm: PBKDF2-HMAC-SHA256 → AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC); works on every .NET platform.
/// </summary>
public interface ISecurePayloadProtector
{
    const string EnvelopePrefix = "QQSEC1:";

    bool IsProtected(string payload);

    /// <summary>CPU-heavy (key derivation) — call off the UI thread.</summary>
    string Protect(string plaintext, string passphrase);

    /// <summary>Fails with <see cref="ErrorCodes.Validation"/> on a wrong passphrase or tampered envelope.</summary>
    Result<string> Unprotect(string envelope, string passphrase);
}
