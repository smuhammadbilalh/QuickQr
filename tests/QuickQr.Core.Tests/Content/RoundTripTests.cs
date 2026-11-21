using QuickQr.Core.Content;

namespace QuickQr.Core.Tests.Content;

/// <summary>Parse(Encode(x)) must give back the same content for every type.</summary>
public class RoundTripTests
{
    private readonly ContentEncoder _encoder = new();
    private readonly ContentParser _parser = new();

    public static TheoryData<string> Samples => ContentSamples.Names;

    [Theory]
    [MemberData(nameof(Samples))]
    public void Parse_of_encode_round_trips(string sample)
    {
        var original = ContentSamples.All[sample];

        var payload = _encoder.Encode(original);
        payload.IsSuccess.Should().BeTrue(payload.IsFailure ? payload.Error.Message : string.Empty);

        var parsed = _parser.Parse(payload.Value);

        parsed.Should().BeOfType(original.GetType());
        parsed.Should().BeEquivalentTo(original, options => options.PreferringRuntimeMemberTypes().ComparingRecordsByMembers());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Encoding_is_stable_after_a_round_trip(string sample)
    {
        var first = _encoder.Encode(ContentSamples.All[sample]).Value;
        var second = _encoder.Encode(_parser.Parse(first)).Value;
        second.Should().Be(first);
    }

    [Fact]
    public void Samples_cover_every_content_type() =>
        ContentSamples.All.Values.Select(c => c.Type).Distinct().Should().BeEquivalentTo(Enum.GetValues<ContentType>());

    [Fact]
    public void Contact_round_trips_with_crlf_replaced_by_lf()
    {
        var payload = _encoder.Encode(ContentSamples.FullContact).Value.Replace("\r\n", "\n", StringComparison.Ordinal);
        _parser.Parse(payload).Should().BeEquivalentTo(ContentSamples.FullContact, o => o.ComparingRecordsByMembers());
    }

    [Fact]
    public void Event_round_trips_through_the_calendar_import_document()
    {
        var original = (CalendarEventContent)ContentSamples.All["event"];
        var ics = new ContentActionProvider().GetActions(original).First(a => a.Kind == ContentActionKind.AddToCalendar).Target!;
        _parser.Parse(ics).Should().Be(original);
    }

    [Fact]
    public void MeCard_converted_to_vcard_keeps_its_details()
    {
        var meCard = (MeCardContent)ContentSamples.All["mecard"];
        var vCard = new ContentActionProvider().GetActions(meCard).First(a => a.Kind == ContentActionKind.AddContact).Target!;

        var contact = _parser.Parse(vCard).Should().BeOfType<ContactContent>().Subject;
        contact.FullName.Should().Be("John Doe");
        contact.Phones.Should().Equal(new ContactPhone("+15551234567"));
        contact.Emails.Should().Equal("john@example.com");
        contact.Website.Should().Be("https://example.com");
        contact.Address!.Street.Should().Be("1 Main St, Springfield");
        contact.Note.Should().Be("Call me: anytime");
    }
}
