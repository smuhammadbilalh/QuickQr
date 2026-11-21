using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using QuickQr.Core.Security;

namespace QuickQr.Core.Tests.Security;

public class SecurePayloadProtectorTests
{
    private const string Prefix = ISecurePayloadProtector.EnvelopePrefix;
    private const string WrongPassphrase = "Wrong passphrase or damaged code.";

    private readonly SecurePayloadProtector _protector = new();

    [Theory]
    [InlineData("Hello, world!", "correct horse battery staple")]
    [InlineData("", "p")]
    [InlineData("Grüße 👋 日本語 \u0000 \r\n tab\t", "pässwörd ✓")]
    public void Round_trips(string plaintext, string passphrase)
    {
        var envelope = _protector.Protect(plaintext, passphrase);

        var result = _protector.Unprotect(envelope, passphrase);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(plaintext);
    }

    [Fact]
    public void Round_trips_a_large_payload()
    {
        var plaintext = string.Concat(Enumerable.Repeat("0123456789abcdef", 256));
        _protector.Unprotect(_protector.Protect(plaintext, "pw"), "pw").Value.Should().Be(plaintext);
    }

    [Fact]
    public void Envelope_has_documented_layout()
    {
        var envelope = _protector.Protect("Secret", "pw");

        envelope.Should().StartWith(Prefix);
        envelope[Prefix.Length..].Should().MatchRegex("^[A-Za-z0-9_-]+$", "base64url without padding");

        var bytes = Base64Url.DecodeFromChars(envelope.AsSpan(Prefix.Length));
        bytes[0].Should().Be(1, "version 1");
        bytes.Length.Should().Be(1 + 16 + 16 + 16 + 32, "one AES block of ciphertext for a short message");
        _protector.IsProtected(envelope).Should().BeTrue();
    }

    [Fact]
    public void Envelope_can_be_opened_with_an_independent_implementation_of_the_documented_algorithm()
    {
        const string passphrase = "interop";
        var envelope = _protector.Protect("Interoperable", passphrase);
        var data = Base64Url.DecodeFromChars(envelope.AsSpan(Prefix.Length));

        var salt = data.AsSpan(1, 16);
        var iv = data.AsSpan(17, 16);
        var ciphertext = data.AsSpan(33, data.Length - 33 - 32);
        var mac = data.AsSpan(data.Length - 32);

        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt.ToArray(), 600_000, HashAlgorithmName.SHA256, 64);
        HMACSHA256.HashData(keys.AsSpan(32), data.AsSpan(0, data.Length - 32)).Should().Equal(mac.ToArray());

        using var aes = Aes.Create();
        aes.Key = keys[..32];
        Encoding.UTF8.GetString(aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7)).Should().Be("Interoperable");
    }

    [Fact]
    public void Each_envelope_uses_a_fresh_salt_and_iv()
    {
        var first = _protector.Protect("same", "pw");
        var second = _protector.Protect("same", "pw");
        first.Should().NotBe(second);
    }

    [Fact]
    public void Wrong_passphrase_fails_with_validation_error()
    {
        var envelope = _protector.Protect("Secret", "right");

        var result = _protector.Unprotect(envelope, "wrong");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation(WrongPassphrase));
    }

    [Theory]
    [InlineData(1)]   // salt
    [InlineData(20)]  // IV
    [InlineData(40)]  // ciphertext
    [InlineData(-1)]  // MAC (last byte)
    public void Tampering_is_detected(int index)
    {
        var envelope = _protector.Protect("Pay EUR 10 to Alice", "pw");
        var data = Base64Url.DecodeFromChars(envelope.AsSpan(Prefix.Length));
        var position = index < 0 ? data.Length + index : index;
        data[position] ^= 0x01;

        var result = _protector.Unprotect(Prefix + Base64Url.EncodeToString(data), "pw");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation(WrongPassphrase));
    }

    [Fact]
    public void Appending_a_block_is_detected()
    {
        var envelope = _protector.Protect("Short", "pw");
        var data = Base64Url.DecodeFromChars(envelope.AsSpan(Prefix.Length)).Concat(new byte[16]).ToArray();
        _protector.Unprotect(Prefix + Base64Url.EncodeToString(data), "pw").Error.Message.Should().Be(WrongPassphrase);
    }

    [Theory]
    [InlineData(Prefix)]
    [InlineData(Prefix + "!!!not-base64!!!")]
    [InlineData(Prefix + "AQID")]
    public void Malformed_envelopes_fail_without_throwing(string envelope) =>
        _protector.Unprotect(envelope, "pw").Error.Should().Be(Error.Validation(WrongPassphrase));

    [Fact]
    public void Truncated_envelope_fails()
    {
        var envelope = _protector.Protect("Hello", "pw");
        _protector.Unprotect(envelope[..^10], "pw").Error.Message.Should().Be(WrongPassphrase);
    }

    [Fact]
    public void Newer_version_is_reported_clearly()
    {
        var data = new byte[1 + 16 + 16 + 16 + 32];
        data[0] = 2;
        var result = _protector.Unprotect(Prefix + Base64Url.EncodeToString(data), "pw");
        result.Error.Code.Should().Be(ErrorCodes.Validation);
        result.Error.Message.Should().Contain("newer version");
    }

    [Fact]
    public void Non_envelope_is_rejected() =>
        _protector.Unprotect("hello", "pw").Error.Should().Be(Error.Validation("This isn't a QuickQr secure code."));

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        var envelope = _protector.Protect("trim me", "pw");
        _protector.Unprotect($"  {envelope}\r\n", "pw").Value.Should().Be("trim me");
    }

    [Fact]
    public void Passphrase_is_unicode_normalised()
    {
        var composed = "café";
        var decomposed = "café";
        var envelope = _protector.Protect("normalised", composed);
        _protector.Unprotect(envelope, decomposed).Value.Should().Be("normalised");
    }

    [Fact]
    public void Passphrase_is_case_sensitive() =>
        _protector.Unprotect(_protector.Protect("x", "Secret"), "secret").IsFailure.Should().BeTrue();

    [Fact]
    public void Empty_passphrase_is_rejected()
    {
        FluentActions.Invoking(() => _protector.Protect("x", "")).Should().Throw<ArgumentException>();
        _protector.Unprotect(Prefix + "AAAA", "").Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        FluentActions.Invoking(() => _protector.Protect(null!, "pw")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => _protector.Protect("x", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => _protector.Unprotect(null!, "pw")).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("QQSEC1:abc", true)]
    [InlineData("  QQSEC1:abc", true)]
    [InlineData("qqsec1:abc", false)]
    [InlineData("https://example.com", false)]
    [InlineData("", false)]
    public void IsProtected_checks_the_prefix(string payload, bool expected) => _protector.IsProtected(payload).Should().Be(expected);

    [Fact]
    public void Protected_payload_is_parsed_as_secure_message()
    {
        var envelope = _protector.Protect("hidden", "pw");
        new Core.Content.ContentParser().Parse(envelope).Should().Be(new Core.Content.SecureMessageContent(envelope));
    }
}
