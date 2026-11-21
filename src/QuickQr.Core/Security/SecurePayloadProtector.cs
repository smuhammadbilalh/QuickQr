using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace QuickQr.Core.Security;

/// <summary>
/// "Secure QR" envelopes: <c>QQSEC1:</c> + base64url (no padding) of
/// <c>version(1) | salt(16) | iv(16) | ciphertext | hmac(32)</c>.
/// Keys: PBKDF2-HMAC-SHA256, 600,000 iterations → 64 bytes (AES-256-CBC key | HMAC-SHA256 key).
/// Encrypt-then-MAC; the MAC covers version, salt, IV and ciphertext and is verified in constant time before decrypting.
/// The passphrase is Unicode-normalised (NFC) so composed and decomposed input derive the same key.
/// </summary>
public sealed class SecurePayloadProtector : ISecurePayloadProtector
{
    internal const byte FormatVersion = 1;
    internal const int Iterations = 600_000;
    internal const int SaltSize = 16;
    internal const int IvSize = 16;
    internal const int KeySize = 32;
    internal const int MacSize = 32;
    internal const int HeaderSize = 1 + SaltSize + IvSize;
    private const int BlockSize = 16;

    internal const string WrongPassphraseMessage = "Wrong passphrase or damaged code.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public bool IsProtected(string payload) =>
        payload is not null && payload.TrimStart().StartsWith(ISecurePayloadProtector.EnvelopePrefix, StringComparison.Ordinal);

    public string Protect(string plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var keys = DeriveKeys(passphrase, salt);
        var encryptionKey = keys[..KeySize];
        try
        {
            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                ciphertext = aes.EncryptCbc(plainBytes, iv, PaddingMode.PKCS7);
            }

            var envelope = new byte[HeaderSize + ciphertext.Length + MacSize];
            envelope[0] = FormatVersion;
            salt.CopyTo(envelope, 1);
            iv.CopyTo(envelope, 1 + SaltSize);
            ciphertext.CopyTo(envelope, HeaderSize);

            var authenticated = envelope.AsSpan(0, HeaderSize + ciphertext.Length);
            HMACSHA256.HashData(keys.AsSpan(KeySize, KeySize), authenticated, envelope.AsSpan(HeaderSize + ciphertext.Length));

            return ISecurePayloadProtector.EnvelopePrefix + Base64Url.EncodeToString(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys);
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Result<string> Unprotect(string envelope, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(passphrase);

        if (passphrase.Length == 0) return Error.Validation("Enter the passphrase.");

        var text = envelope.Trim();
        if (!text.StartsWith(ISecurePayloadProtector.EnvelopePrefix, StringComparison.Ordinal))
            return Error.Validation("This isn't a QuickQr secure code.");

        byte[] data;
        try
        {
            data = Base64Url.DecodeFromChars(text.AsSpan(ISecurePayloadProtector.EnvelopePrefix.Length));
        }
        catch (FormatException)
        {
            return Damaged();
        }

        var cipherLength = data.Length - HeaderSize - MacSize;
        if (cipherLength < BlockSize || cipherLength % BlockSize != 0 || data[0] == 0) return Damaged();
        if (data[0] > FormatVersion)
            return Error.Validation("This secure code was made with a newer version of QuickQr. Update the app to read it.");

        var salt = data.AsSpan(1, SaltSize).ToArray();
        var keys = DeriveKeys(passphrase, salt);
        var encryptionKey = keys[..KeySize];
        byte[]? plainBytes = null;
        try
        {
            Span<byte> expectedMac = stackalloc byte[MacSize];
            HMACSHA256.HashData(keys.AsSpan(KeySize, KeySize), data.AsSpan(0, HeaderSize + cipherLength), expectedMac);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, data.AsSpan(HeaderSize + cipherLength, MacSize)))
                return Damaged();

            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                plainBytes = aes.DecryptCbc(data.AsSpan(HeaderSize, cipherLength), data.AsSpan(1 + SaltSize, IvSize), PaddingMode.PKCS7);
            }

            return StrictUtf8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return Damaged();
        }
        catch (DecoderFallbackException)
        {
            return Damaged();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys);
            CryptographicOperations.ZeroMemory(encryptionKey);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static Error Damaged() => Error.Validation(WrongPassphraseMessage);

    private static byte[] DeriveKeys(string passphrase, byte[] salt)
    {
        var keys = new byte[KeySize * 2];
        Rfc2898DeriveBytes.Pbkdf2(Normalize(passphrase), salt, keys, Iterations, HashAlgorithmName.SHA256);
        return keys;
    }

    private static string Normalize(string passphrase)
    {
        try
        {
            return passphrase.IsNormalized(NormalizationForm.FormC) ? passphrase : passphrase.Normalize(NormalizationForm.FormC);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException)
        {
            // Invariant-globalization builds or invalid UTF-16: derive from the raw string.
            return passphrase;
        }
    }
}
