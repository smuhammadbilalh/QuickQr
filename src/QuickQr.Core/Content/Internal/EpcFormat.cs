using System.Text;
using System.Text.RegularExpressions;

namespace QuickQr.Core.Content.Internal;

/// <summary>EPC069-12 SEPA credit transfer ("GiroCode", "EPC QR"), version 002, UTF-8, LF separated.</summary>
internal static partial class EpcFormat
{
    public const int MaxBytes = 331;
    public const decimal MinAmount = 0.01m;
    public const decimal MaxAmount = 999_999_999.99m;

    [GeneratedRegex(@"^[A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex BicRegex();

    [GeneratedRegex(@"^[A-Z0-9]{1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex PurposeRegex();

    public static string NormalizeBic(string? bic) => Validators.NormalizeIban(bic);

    private static string Field(string? value) => PayloadText.Clean(value) is { } v ? PayloadText.SingleLine(v).Trim() : string.Empty;

    private static string Reference(string? value) => Validators.NormalizeIban(value);

    public static string Write(EpcPaymentContent content)
    {
        var lines = new List<string>
        {
            "BCD",
            "002",
            "1", // UTF-8
            "SCT",
            NormalizeBic(content.Bic),
            Field(content.BeneficiaryName),
            Validators.NormalizeIban(content.Iban),
            content.Amount is { } amount ? "EUR" + PayloadText.FormatMoney(amount) : string.Empty,
            Field(content.Purpose).ToUpperInvariant(),
            Reference(content.StructuredReference),
            Field(content.RemittanceText),
        };

        // The last populated element must not be followed by separators.
        while (lines.Count > 7 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }

    public static Error? Validate(EpcPaymentContent content)
    {
        var name = Field(content.BeneficiaryName);
        if (name.Length == 0) return Error.Validation("Enter the beneficiary's name.");
        if (name.Length > 70) return Error.Validation("The beneficiary name can be at most 70 characters.");

        var iban = Validators.NormalizeIban(content.Iban);
        if (iban.Length == 0) return Error.Validation("Enter the IBAN.");
        if (!Validators.IsValidIban(iban)) return Error.Validation("That IBAN isn't valid. Please check it.");

        var bic = NormalizeBic(content.Bic);
        if (bic.Length > 0 && !BicRegex().IsMatch(bic)) return Error.Validation("That BIC isn't valid. It has 8 or 11 letters and digits.");

        if (content.Amount is { } amount)
        {
            if (amount is < MinAmount or > MaxAmount)
                return Error.Validation("The amount must be between EUR 0.01 and EUR 999,999,999.99.");
            if (PayloadText.DecimalPlacesNormalized(amount) > 2)
                return Error.Validation("Use at most two decimal places for the amount.");
        }

        var purpose = Field(content.Purpose).ToUpperInvariant();
        if (purpose.Length > 0 && !PurposeRegex().IsMatch(purpose))
            return Error.Validation("The purpose code has up to 4 letters or digits, e.g. GDDS.");

        var reference = Reference(content.StructuredReference);
        var remittance = Field(content.RemittanceText);
        if (reference.Length > 0 && remittance.Length > 0)
            return Error.Validation("Use either a structured reference or a remittance text, not both.");
        if (reference.Length > 35) return Error.Validation("The structured reference can be at most 35 characters.");
        if (remittance.Length > 140) return Error.Validation("The remittance text can be at most 140 characters.");

        return Encoding.UTF8.GetByteCount(Write(content)) > MaxBytes
            ? Error.Validation("The payment details are too long for a SEPA QR code.")
            : null;
    }

    public static bool IsEpc(string payload)
    {
        if (!payload.StartsWith("BCD", StringComparison.OrdinalIgnoreCase)) return false;
        var lines = PayloadText.SplitLines(payload);
        return lines.Length >= 7 && lines[0].Trim().Equals("BCD", StringComparison.OrdinalIgnoreCase) && lines[1].Trim() is "001" or "002";
    }

    public static QrContent? TryParse(string payload)
    {
        var lines = PayloadText.SplitLines(payload).Select(l => l.Trim()).ToArray();
        string? Line(int index) => index < lines.Length && lines[index].Length > 0 ? lines[index] : null;

        var name = Line(5);
        var iban = Line(6);
        if (name is null || iban is null) return null;

        decimal? amount = null;
        if (Line(7) is { } amountText)
        {
            var number = amountText.StartsWith("EUR", StringComparison.OrdinalIgnoreCase) ? amountText[3..] : amountText;
            if (PayloadText.TryParseDecimal(number, out var parsed) && parsed > 0) amount = parsed;
        }

        return new EpcPaymentContent(
            BeneficiaryName: name,
            Iban: Validators.NormalizeIban(iban),
            Bic: Line(4) is { } bic ? NormalizeBic(bic) : null,
            Amount: amount,
            Purpose: Line(8),
            StructuredReference: Line(9),
            RemittanceText: Line(10));
    }

    /// <summary>Plain-text block for copying the transfer details.</summary>
    public static string Describe(EpcPaymentContent content)
    {
        var lines = new List<string> { "Beneficiary: " + Field(content.BeneficiaryName), "IBAN: " + Validators.FormatIban(Validators.NormalizeIban(content.Iban)) };
        if (NormalizeBic(content.Bic) is { Length: > 0 } bic) lines.Add("BIC: " + bic);
        if (content.Amount is { } amount) lines.Add("Amount: EUR " + PayloadText.FormatMoney(amount));
        if (Reference(content.StructuredReference) is { Length: > 0 } reference) lines.Add("Reference: " + reference);
        if (Field(content.RemittanceText) is { Length: > 0 } remittance) lines.Add("Remittance: " + remittance);
        return string.Join('\n', lines);
    }
}
