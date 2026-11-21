using CommunityToolkit.Mvvm.ComponentModel;
using QuickQr.Controls;
using QuickQr.Core.Content;

namespace QuickQr.Features.Generate;

public sealed partial class CryptoFormViewModel : ContentFormViewModel
{
    public CryptoFormViewModel()
    {
        Currency = new OptionGroup<CryptoCurrency>()
            .Add(CryptoCurrency.Bitcoin, "Bitcoin")
            .Add(CryptoCurrency.Ethereum, "Ethereum")
            .Add(CryptoCurrency.Litecoin, "Litecoin")
            .Add(CryptoCurrency.BitcoinCash, "Bitcoin Cash");
        Currency.SelectionChanged += (_, _) => OnPropertyChanged(nameof(Currency));
    }

    public override ContentKind Kind => ContentKind.Crypto;

    public OptionGroup<CryptoCurrency> Currency { get; }

    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Amount { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Address);

    public override string? Validate() =>
        TryParseDecimal(Amount, out var amount) && amount is null or > 0m ? null : "Enter a valid amount, for example 0.0015.";

    public override QrContent BuildContent()
    {
        TryParseDecimal(Amount, out var amount);
        return new CryptoPaymentContent(Currency.Value, Required(Address), amount, Optional(Label), Optional(Message));
    }

    public override bool TryLoad(QrContent content)
    {
        if (content is not CryptoPaymentContent crypto) return false;
        Currency.Value = crypto.Currency;
        Address = crypto.Address;
        Amount = Format(crypto.Amount);
        Label = crypto.Label ?? string.Empty;
        Message = crypto.Message ?? string.Empty;
        return true;
    }
}

/// <summary>SEPA credit transfer ("GiroCode", EPC069-12).</summary>
public sealed partial class SepaFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Sepa;

    [ObservableProperty]
    public partial string BeneficiaryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Iban { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Bic { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Amount { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Reference { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(BeneficiaryName, Iban);

    public override string? Validate() =>
        TryParseDecimal(Amount, out var amount) && amount is null or > 0m ? null : "Enter a valid amount in euros, for example 25.50.";

    public override QrContent BuildContent()
    {
        TryParseDecimal(Amount, out var amount);
        var iban = Required(Iban).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var reference = Optional(Reference);
        // An ISO 11649 creditor reference ("RF…") goes in the structured field; anything else is free text.
        var structured = reference is not null && reference.StartsWith("RF", StringComparison.OrdinalIgnoreCase) && !reference.Contains(' ', StringComparison.Ordinal)
            ? reference.ToUpperInvariant()
            : null;
        return new EpcPaymentContent(
            Required(BeneficiaryName),
            iban,
            Optional(Bic)?.ToUpperInvariant(),
            amount,
            StructuredReference: structured,
            RemittanceText: structured is null ? reference : null);
    }

    public override bool TryLoad(QrContent content)
    {
        if (content is not EpcPaymentContent epc) return false;
        BeneficiaryName = epc.BeneficiaryName;
        Iban = epc.Iban;
        Bic = epc.Bic ?? string.Empty;
        Amount = Format(epc.Amount);
        Reference = epc.StructuredReference ?? epc.RemittanceText ?? string.Empty;
        return true;
    }
}

public sealed partial class UpiFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Upi;

    [ObservableProperty]
    public partial string PayeeAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PayeeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Amount { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(PayeeAddress, PayeeName);

    public override string? Validate() =>
        TryParseDecimal(Amount, out var amount) && amount is null or > 0m ? null : "Enter a valid amount in rupees, for example 250.";

    public override QrContent BuildContent()
    {
        TryParseDecimal(Amount, out var amount);
        return new UpiPaymentContent(Required(PayeeAddress), Required(PayeeName), amount, Optional(Note));
    }

    public override bool TryLoad(QrContent content)
    {
        if (content is not UpiPaymentContent upi) return false;
        PayeeAddress = upi.PayeeAddress;
        PayeeName = upi.PayeeName;
        Amount = Format(upi.Amount);
        Note = upi.Note ?? string.Empty;
        return true;
    }
}
