using QuickQr.Core.Batch;
using QuickQr.Infrastructure.Batch;

namespace QuickQr.Infrastructure.Tests.Batch;

public sealed class BatchInputParserTests
{
    private readonly BatchInputParser _parser = new();

    [Fact]
    public void Parses_csv_with_header_payload_and_name_columns()
    {
        var result = _parser.Parse("payload,name\r\nhttps://a.test,Alpha\r\nhttps://b.test,Beta\r\n");

        result.Items.Should().Equal(new BatchItem("https://a.test", "Alpha"), new BatchItem("https://b.test", "Beta"));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Handles_quotes_escaped_quotes_embedded_delimiters_and_newlines()
    {
        const string csv = "payload,name\n\"BEGIN:VCARD\nFN:Ann, \"\"Boss\"\"\nEND:VCARD\",\"Ann, Inc\"\n";

        var result = _parser.Parse(csv);

        result.Items.Should().ContainSingle()
            .Which.Should().Be(new BatchItem("BEGIN:VCARD\nFN:Ann, \"Boss\"\nEND:VCARD", "Ann, Inc"));
    }

    [Theory]
    [InlineData("a;b\nc;d\n", ';')]
    [InlineData("a\tb\nc\td\n", '\t')]
    [InlineData("a,b\nc,d\n", ',')]
    [InlineData("\"x;y\",b\n\"z;w\",d\n", ',')]
    public void Detects_the_delimiter(string text, char expected) =>
        BatchInputParser.DetectDelimiter(text).Should().Be(expected);

    [Fact]
    public void Plain_lines_get_generated_names()
    {
        var result = _parser.Parse("first\nsecond\r\nthird", new BatchParseOptions(HasHeaderRow: false));

        result.Items.Should().Equal(
            new BatchItem("first", "code-0001"),
            new BatchItem("second", "code-0002"),
            new BatchItem("third", "code-0003"));
    }

    [Fact]
    public void Urls_with_occasional_separators_stay_single_column()
    {
        var text = "https://a.test/x;y\nhttps://b.test\nhttps://c.test/p,q\nhttps://d.test\nhttps://e.test\n";

        BatchInputParser.DetectDelimiter(text).Should().BeNull();
        var result = _parser.Parse(text, new BatchParseOptions(HasHeaderRow: false));
        result.Items.Select(i => i.Payload).Should().Equal("https://a.test/x;y", "https://b.test", "https://c.test/p,q", "https://d.test", "https://e.test");
    }

    [Fact]
    public void Single_column_csv_uses_generated_names()
    {
        var result = _parser.Parse("payload\nhello\nworld\n");

        result.Items.Should().Equal(new BatchItem("hello", "code-0001"), new BatchItem("world", "code-0002"));
    }

    [Fact]
    public void Honours_explicit_delimiter_and_columns()
    {
        var result = _parser.Parse("x|ignored|one\ny|ignored|two", new BatchParseOptions(HasHeaderRow: false, Delimiter: '|', PayloadColumn: 2, FileNameColumn: 0));

        result.Items.Should().Equal(new BatchItem("one", "x"), new BatchItem("two", "y"));
    }

    [Fact]
    public void Without_name_column_every_item_is_numbered()
    {
        var result = _parser.Parse("a,n1\nb,n2", new BatchParseOptions(HasHeaderRow: false, FileNameColumn: null));

        result.Items.Select(i => i.FileName).Should().Equal("code-0001", "code-0002");
    }

    [Fact]
    public void Skips_blank_lines_and_warns_about_empty_payloads()
    {
        var result = _parser.Parse("payload,name\n\n   \n,orphan\nvalue,ok\n,\n");

        result.Items.Should().Equal(new BatchItem("value", "ok"));
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Line 4").And.Contain("empty payload");
    }

    [Fact]
    public void Sanitises_file_names()
    {
        var result = _parser.Parse(
            "p1,a/b:c*d?\np2,CON\np3,  ..trailing dots..  \np4,<>|\np5,lpt1.txt\np6," + new string('x', 120) + "\np7,tab\there\n",
            new BatchParseOptions(HasHeaderRow: false));

        result.Items.Select(i => i.FileName).Should().Equal(
            "a_b_c_d_",
            "_CON",
            "trailing dots",
            "code-0004",
            "_lpt1.txt",
            new string('x', 80),
            "tab_here");
    }

    [Fact]
    public void Deduplicates_names_case_insensitively()
    {
        var result = _parser.Parse("1,Menu\n2,menu\n3,Menu\n4,Menu-2\n", new BatchParseOptions(HasHeaderRow: false));

        result.Items.Select(i => i.FileName).Should().Equal("Menu", "menu-2", "Menu-3", "Menu-2-2");
    }

    [Fact]
    public void Deduplicated_long_names_stay_within_the_limit()
    {
        var name = new string('n', 100);
        var result = _parser.Parse($"1,{name}\n2,{name}\n", new BatchParseOptions(HasHeaderRow: false));

        result.Items[1].FileName.Should().HaveLength(80).And.EndWith("-2");
    }

    [Fact]
    public void Caps_items_with_a_warning()
    {
        var text = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"item{i}"));

        var result = _parser.Parse(text, new BatchParseOptions(HasHeaderRow: false, MaxItems: 10));

        result.Items.Should().HaveCount(10);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("first 10").And.Contain("2 more");
    }

    [Fact]
    public void Tolerates_bom_whitespace_and_unterminated_quotes()
    {
        var result = _parser.Parse("﻿payload,name\n  \"quoted\" , named \n\"open quote,never closed");

        result.Items.Should().HaveCount(2);
        result.Items[0].Should().Be(new BatchItem("quoted", "named"));
        result.Items[1].Payload.Should().Be("open quote,never closed");
    }

    [Fact]
    public void Keeps_stray_quotes_inside_unquoted_fields()
    {
        var result = _parser.Parse("{\"a\":1}\n", new BatchParseOptions(HasHeaderRow: false, Delimiter: '\t'));

        result.Items.Should().ContainSingle().Which.Payload.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void Empty_input_returns_nothing()
    {
        _parser.Parse(string.Empty).Items.Should().BeEmpty();
        _parser.Parse("\n\n\n", new BatchParseOptions(HasHeaderRow: false)).Items.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_invalid_options()
    {
        var act = () => _parser.Parse("x", new BatchParseOptions(MaxItems: 0));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
