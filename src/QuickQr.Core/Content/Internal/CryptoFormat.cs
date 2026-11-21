using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>Crypto payment requests: BIP21 (<c>bitcoin:</c>, <c>litecoin:</c>, <c>bitcoincash:</c>) and EIP-681 (<c>ethereum:</c>).</summary>
internal static partial class CryptoFormat
{
    private const decimal WeiPerEther = 1_000_000_000_000_000_000m;

    // Bech32 data characters exclude 1, b, i and o.
    private const string Bech32 = "[qpzry9x8gf2tvdw0s3jn54khce6mua7l]";
    private const string Base58 = "[1-9A-HJ-NP-Za-km-z]";

    [GeneratedRegex($"^(?:(?:bc1|tb1|bcrt1){Bech32}{{6,87}}|[123mn]{Base58}{{25,34}})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BitcoinAddress();

    [GeneratedRegex($"^(?:(?:ltc1|tltc1){Bech32}{{6,87}}|[LM3mnQ2]{Base58}{{25,34}})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LitecoinAddress();

    [GeneratedRegex($"^(?:[qp]{Bech32}{{40,111}}|[13]{Base58}{{25,34}})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BitcoinCashAddress();

    [GeneratedRegex(@"^(?:0x[0-9a-fA-F]{40}|(?:[a-z0-9-]+\.)+eth)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EthereumAddress();

    public static string Scheme(CryptoCurrency currency) => currency switch
    {
        CryptoCurrency.Ethereum => "ethereum",
        CryptoCurrency.Litecoin => "litecoin",
        CryptoCurrency.BitcoinCash => "bitcoincash",
        _ => "bitcoin",
    };

    public static string DisplayName(CryptoCurrency currency) => currency switch
    {
        CryptoCurrency.Ethereum => "Ethereum",
        CryptoCurrency.Litecoin => "Litecoin",
        CryptoCurrency.BitcoinCash => "Bitcoin Cash",
        _ => "Bitcoin",
    };

    public static string Ticker(CryptoCurrency currency) => currency switch
    {
        CryptoCurrency.Ethereum => "ETH",
        CryptoCurrency.Litecoin => "LTC",
        CryptoCurrency.BitcoinCash => "BCH",
        _ => "BTC",
    };

    /// <summary>Trims and removes a pasted scheme prefix ("bitcoincash:qq…").</summary>
    public static string NormalizeAddress(CryptoCurrency currency, string? address)
    {
        var value = (address ?? string.Empty).Trim();
        var prefix = Scheme(currency) + ":";
        if (PayloadText.StartsWithIgnoreCase(value, prefix)) value = value[prefix.Length..];
        var query = value.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? value[..query] : value;
    }

    public static string Write(CryptoPaymentContent content)
    {
        var address = NormalizeAddress(content.Currency, content.Address);
        var parameters = new List<string>(3);

        if (content.Currency == CryptoCurrency.Ethereum)
        {
            if (content.Amount is { } eth && eth > 0 && TryToWei(eth, out var wei)) parameters.Add("value=" + wei);
        }
        else
        {
            if (content.Amount is { } amount && amount > 0) parameters.Add("amount=" + PayloadText.FormatDecimal(amount));
            if (PayloadText.Clean(content.Label) is { } label) parameters.Add("label=" + PayloadText.PercentEncode(label));
            if (PayloadText.Clean(content.Message) is { } message) parameters.Add("message=" + PayloadText.PercentEncode(message));
        }

        var uri = $"{Scheme(content.Currency)}:{address}";
        return parameters.Count == 0 ? uri : $"{uri}?{string.Join('&', parameters)}";
    }

    private static bool TryToWei(decimal ether, out string wei)
    {
        try
        {
            wei = decimal.Truncate(ether * WeiPerEther).ToString("0", PayloadText.Invariant);
            return true;
        }
        catch (OverflowException)
        {
            wei = string.Empty;
            return false;
        }
    }

    public static Error? Validate(CryptoPaymentContent content)
    {
        var address = NormalizeAddress(content.Currency, content.Address);
        var name = DisplayName(content.Currency);
        if (address.Length == 0) return Error.Validation("Enter the wallet address.");

        var valid = content.Currency switch
        {
            CryptoCurrency.Bitcoin => BitcoinAddress().IsMatch(address),
            CryptoCurrency.Litecoin => LitecoinAddress().IsMatch(address),
            CryptoCurrency.BitcoinCash => BitcoinCashAddress().IsMatch(address),
            CryptoCurrency.Ethereum => EthereumAddress().IsMatch(address),
            _ => false,
        };
        if (!valid) return Error.Validation($"That doesn't look like a valid {name} address.");

        if (content.Amount is { } amount)
        {
            if (amount <= 0) return Error.Validation("The amount must be greater than zero.");
            var maxDecimals = content.Currency == CryptoCurrency.Ethereum ? 18 : 8;
            if (PayloadText.DecimalPlacesNormalized(amount) > maxDecimals)
                return Error.Validation($"{name} amounts can have at most {maxDecimals} decimal places.");
            if (content.Currency == CryptoCurrency.Ethereum && !TryToWei(amount, out _))
                return Error.Validation("The amount is too large.");
        }
        return null;
    }

    public static bool TryGetCurrency(string payload, out CryptoCurrency currency)
    {
        foreach (var candidate in new[] { CryptoCurrency.BitcoinCash, CryptoCurrency.Bitcoin, CryptoCurrency.Ethereum, CryptoCurrency.Litecoin })
        {
            if (PayloadText.StartsWithIgnoreCase(payload, Scheme(candidate) + ":"))
            {
                currency = candidate;
                return true;
            }
        }
        currency = default;
        return false;
    }

    public static QrContent? TryParse(CryptoCurrency currency, string payload)
    {
        var rest = payload[(Scheme(currency).Length + 1)..];
        if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];

        var questionMark = rest.IndexOf('?', StringComparison.Ordinal);
        var target = PayloadText.PercentDecode(questionMark < 0 ? rest : rest[..questionMark]).Trim();
        var query = questionMark < 0 ? [] : PayloadText.ParseQuery(rest[(questionMark + 1)..], plusAsSpace: false);
        var label = PayloadText.Clean(PayloadText.GetValue(query, "label"));
        var message = PayloadText.Clean(PayloadText.GetValue(query, "message"));

        if (currency == CryptoCurrency.Ethereum) return ParseEthereum(target, query, label, message);

        // Bech32 addresses are case-insensitive; QR codes often upper-case them for alphanumeric mode.
        if (target.StartsWith("BC1", StringComparison.Ordinal) || target.StartsWith("LTC1", StringComparison.Ordinal) ||
            target.StartsWith("TB1", StringComparison.Ordinal) || (currency == CryptoCurrency.BitcoinCash && target.Length > 40 && target.All(c => !char.IsLower(c))))
        {
            target = target.ToLowerInvariant();
        }

        decimal? amount = PayloadText.TryParseDecimal(PayloadText.GetValue(query, "amount"), out var parsed) && parsed > 0 ? parsed : null;
        return target.Length == 0 ? null : new CryptoPaymentContent(currency, target, amount, label, message);
    }

    private static QrContent? ParseEthereum(string target, List<KeyValuePair<string, string>> query, string? label, string? message)
    {
        // EIP-681: ethereum:[pay-]<address>[@chainId][/function]?params
        if (target.StartsWith("pay-", StringComparison.OrdinalIgnoreCase)) target = target[4..];
        var slash = target.IndexOf('/', StringComparison.Ordinal);
        var function = slash >= 0 ? target[(slash + 1)..] : null;
        if (slash >= 0) target = target[..slash];
        var at = target.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0) target = target[..at];

        decimal? amount = null;
        if (function is null || function.Length == 0)
        {
            if (PayloadText.TryParseDecimal(PayloadText.GetValue(query, "value"), out var wei) && wei > 0)
                amount = wei / WeiPerEther;
        }
        else if (function.Equals("transfer", StringComparison.OrdinalIgnoreCase) &&
                 PayloadText.Clean(PayloadText.GetValue(query, "address")) is { } recipient)
        {
            // ERC-20 transfer: pay the recipient; the token amount is in token units, so it is not shown as ETH.
            target = recipient;
        }

        return target.Length == 0 ? null : new CryptoPaymentContent(CryptoCurrency.Ethereum, target, amount, label, message);
    }
}
