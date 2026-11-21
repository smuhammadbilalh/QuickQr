using QuickQr.Core.Content.Internal;

namespace QuickQr.Core.Tests.Content;

public class ValidatorsTests
{
    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("NL91ABNA0417164300", true)]
    [InlineData("FR1420041010050500013M02606", true)]
    [InlineData("NO9386011117947", true)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("DE8937040044053201300", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("DE89 3704", false)]
    public void Iban_checksum(string iban, bool valid) => Validators.IsValidIban(iban).Should().Be(valid);

    [Fact]
    public void Iban_is_normalised_and_formatted()
    {
        Validators.NormalizeIban(" de89 3704-0044 0532 0130 00 ").Should().Be("DE89370400440532013000");
        Validators.FormatIban("DE89370400440532013000").Should().Be("DE89 3704 0044 0532 0130 00");
    }

    [Theory]
    [InlineData("4006381333931", true)]
    [InlineData("9780306406157", true)]
    [InlineData("96385074", true)]
    [InlineData("036000291452", true)]
    [InlineData("01234565", true)]
    [InlineData("04252614", true)]
    [InlineData("4006381333930", false)]
    [InlineData("036000291453", false)]
    [InlineData("12345678", false)]
    [InlineData("123456789", false)]
    [InlineData("40063813339a1", false)]
    public void Product_check_digits(string code, bool valid) => Validators.IsValidProductCode(code).Should().Be(valid);

    [Theory]
    [InlineData("04252614", "042100005264")]
    [InlineData("01234565", "012345000065")]
    [InlineData("01234133", "012300000413")]
    [InlineData("01234144", "012340000014")]
    public void Upc_e_expands_to_upc_a(string upcE, string upcA) => Validators.ExpandUpcE(upcE).Should().Be(upcA);

    [Theory]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    [InlineData("0044 20.7946/0958", "00442079460958")]
    [InlineData("555 1234", "5551234")]
    [InlineData("12", null)]
    [InlineData("1-800-FLOWERS", null)]
    [InlineData("++15551234", null)]
    [InlineData("", null)]
    public void Phone_normalisation(string input, string? expected) => Validators.NormalizePhone(input).Should().Be(expected);

    [Theory]
    [InlineData("john@example.com", true)]
    [InlineData("john.doe+tag@sub.example.co.uk", true)]
    [InlineData("o'brien@example.ie", true)]
    [InlineData("用户@例子.广告", true)]
    [InlineData("john@localhost", false)]
    [InlineData("john@@example.com", false)]
    [InlineData("john doe@example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("john@", false)]
    public void Email_validation(string email, bool valid) => Validators.IsEmail(email).Should().Be(valid);
}
