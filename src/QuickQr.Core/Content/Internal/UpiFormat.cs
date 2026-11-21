using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>NPCI UPI payment links: <c>upi://pay?pa=..&amp;pn=..&amp;am=..&amp;cu=INR&amp;tn=..</c>.</summary>
internal static partial class UpiFormat
{
    public const string Prefix = "upi://pay";

    [GeneratedRegex(@"^[A-Za-z0-9.\-_]{2,256}@[A-Za-z0-9.\-]{2,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex VpaRegex();

    [GeneratedRegex(@"^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyRegex();

    public static string Write(UpiPaymentContent content)
    {
        var parameters = new List<string>
        {
            "pa=" + PayloadText.PercentEncode((content.PayeeAddress ?? string.Empty).Trim(), "@"),
            "pn=" + PayloadText.PercentEncode((content.PayeeName ?? string.Empty).Trim()),
        };
        if (content.Amount is { } amount && amount > 0) parameters.Add("am=" + PayloadText.FormatMoney(amount));
        parameters.Add("cu=" + Currency(content));
        if (PayloadText.Clean(content.Note) is { } note) parameters.Add("tn=" + PayloadText.PercentEncode(PayloadText.SingleLine(note)));
        return $"{Prefix}?{string.Join('&', parameters)}";
    }

    private static string Currency(UpiPaymentContent content) =>
        PayloadText.Clean(content.Currency)?.ToUpperInvariant() ?? "INR";

    public static Error? Validate(UpiPaymentContent content)
    {
        var vpa = (content.PayeeAddress ?? string.Empty).Trim();
        if (vpa.Length == 0) return Error.Validation("Enter the payee's UPI ID.");
        if (!VpaRegex().IsMatch(vpa)) return Error.Validation("Enter a valid UPI ID, e.g. name@bank.");
        if (string.IsNullOrWhiteSpace(content.PayeeName)) return Error.Validation("Enter the payee's name.");
        if (content.Amount is { } amount)
        {
            if (amount <= 0) return Error.Validation("The amount must be greater than zero.");
            if (PayloadText.DecimalPlacesNormalized(amount) > 2) return Error.Validation("Use at most two decimal places for the amount.");
        }
        return CurrencyRegex().IsMatch(Currency(content)) ? null : Error.Validation("The currency must be a 3-letter code such as INR.");
    }

    public static QrContent? TryParse(string payload)
    {
        var questionMark = payload.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0) return null;
        var query = PayloadText.ParseQuery(payload[(questionMark + 1)..], plusAsSpace: true);

        var payee = PayloadText.Clean(PayloadText.GetValue(query, "pa"));
        if (payee is null) return null;

        decimal? amount = PayloadText.TryParseDecimal(PayloadText.GetValue(query, "am"), out var parsed) && parsed > 0 ? parsed : null;
        return new UpiPaymentContent(
            payee,
            PayloadText.Clean(PayloadText.GetValue(query, "pn")) ?? string.Empty,
            amount,
            PayloadText.Clean(PayloadText.GetValue(query, "tn")),
            PayloadText.Clean(PayloadText.GetValue(query, "cu"))?.ToUpperInvariant() ?? "INR");
    }
}
