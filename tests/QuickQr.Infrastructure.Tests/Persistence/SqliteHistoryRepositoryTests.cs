using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Infrastructure.Persistence;
using QuickQr.Infrastructure.Tests.Fakes;

namespace QuickQr.Infrastructure.Tests.Persistence;

public sealed class SqliteHistoryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _temp = new();
    private readonly List<SqliteHistoryRepository> _repositories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var repository in _repositories) repository.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Default_path_is_in_app_data_and_honours_options()
    {
        var appInfo = new FakeAppInfoProvider(_temp.Path);
        var byDefault = Track(new SqliteHistoryRepository(Options.Create(new QuickQrInfrastructureOptions()), appInfo));
        byDefault.DatabasePath.Should().Be(Path.Combine(_temp.Path, "quickqr.db3"));

        var custom = _temp.File(Path.Combine("nested", "custom.db3"));
        var configured = Track(new SqliteHistoryRepository(Options.Create(new QuickQrInfrastructureOptions { DatabasePath = custom }), appInfo));
        await configured.InitializeAsync(Ct);

        configured.DatabasePath.Should().Be(custom);
        File.Exists(custom).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_uses_wal_sets_schema_version_and_is_idempotent()
    {
        var path = _temp.File("history.db3");
        var first = Create(path);
        await first.InitializeAsync(Ct);
        await first.InitializeAsync(Ct);
        await first.AddAsync(Entry("kept"), Ct);
        first.Dispose();

        var second = Create(path);
        await second.InitializeAsync(Ct);

        (await second.CountAsync(cancellationToken: Ct)).Should().Be(1);
        second.IsFullTextSearchEnabled.Should().BeTrue();
        (await second.QueryAsync(new HistoryQuery(Search: "kept"), Ct)).Should().ContainSingle();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(Ct);
        (await ScalarAsync(connection, "PRAGMA journal_mode;")).Should().Be("wal");
        (await ScalarAsync(connection, "PRAGMA user_version;")).Should().Be(SqliteHistoryRepository.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Crud_round_trips_every_field()
    {
        var repository = Create();
        var entry = new HistoryEntry
        {
            Kind = HistoryKind.Generated,
            Payload = "WIFI:T:WPA;S:Home;P:secret;;",
            ContentType = ContentType.Wifi,
            Format = BarcodeFormat.QrCode,
            Title = "Home",
            Subtitle = "WPA",
            CreatedAt = T0,
            IsFavorite = true,
            Notes = "Living room",
            Tags = "home,wifi",
            Source = ScanSource.None,
            StyleJson = """{"foregroundColor":"#112233"}"""
        };

        await repository.AddAsync(entry, Ct);
        (await repository.GetAsync(entry.Id, Ct)).Should().Be(entry);

        var updated = entry with { Title = "Office", IsFavorite = false, Notes = null, Tags = null };
        await repository.UpdateAsync(updated, Ct);
        (await repository.GetAsync(entry.Id, Ct)).Should().Be(updated);

        await repository.DeleteAsync(entry.Id, Ct);
        (await repository.GetAsync(entry.Id, Ct)).Should().BeNull();
        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Add_with_existing_id_updates_instead_of_failing()
    {
        var repository = Create();
        var entry = Entry("first", payload: "payload");
        await repository.AddAsync(entry, Ct);
        await repository.AddAsync(entry with { Title = "second" }, Ct);

        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(1);
        (await repository.QueryAsync(new HistoryQuery(Search: "second"), Ct)).Should().ContainSingle();
        (await repository.QueryAsync(new HistoryQuery(Search: "first"), Ct)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_matches_prefixes_across_columns(bool fullText)
    {
        var repository = Create(fullText: fullText);
        await repository.AddAsync(Entry("Company website", payload: "https://example.com/products"), Ct);
        await repository.AddAsync(Entry("Guest wifi", payload: "WIFI:S:Guest;;", notes: "Lobby printer"), Ct);
        await repository.AddAsync(Entry("Café menu", payload: "https://menu.test", tags: "food,restaurant"), Ct);

        repository.IsFullTextSearchEnabled.Should().Be(fullText);
        (await Titles(repository, "webs")).Should().Equal("Company website");
        (await Titles(repository, "example.com")).Should().Equal("Company website");
        (await Titles(repository, "lobby")).Should().Equal("Guest wifi");
        (await Titles(repository, "restaur")).Should().Equal("Café menu");
        (await Titles(repository, "guest wifi")).Should().Equal("Guest wifi");
        (await Titles(repository, "guest missing")).Should().BeEmpty();
    }

    [Fact]
    public async Task Full_text_search_ignores_diacritics()
    {
        var repository = Create();
        await repository.AddAsync(Entry("Café menu"), Ct);

        (await Titles(repository, "cafe")).Should().Equal("Café menu");
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"unterminated")]
    [InlineData("*")]
    [InlineData("a*b")]
    [InlineData("NEAR(")]
    [InlineData("AND OR NOT")]
    [InlineData("title:x")]
    [InlineData("-minus")]
    [InlineData("^caret")]
    [InlineData("'; DROP TABLE history; --")]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    [InlineData("(((")]
    [InlineData("{}[]")]
    public async Task Search_with_special_characters_never_throws(string search)
    {
        foreach (var fullText in new[] { true, false })
        {
            var repository = Create(_temp.File($"special-{fullText}-{Guid.NewGuid():N}.db3"), fullText);
            await repository.AddAsync(Entry("Plain"), Ct);

            var act = () => repository.QueryAsync(new HistoryQuery(Search: search), Ct);

            await act.Should().NotThrowAsync();
            (await repository.CountAsync(cancellationToken: Ct)).Should().Be(1, "the table must survive any search text");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_finds_literal_special_characters(bool fullText)
    {
        var repository = Create(fullText: fullText);
        await repository.AddAsync(Entry("Discount", payload: "SAVE 100% NOW"), Ct);
        await repository.AddAsync(Entry("Mail", payload: "mailto:a_b@example.com"), Ct);
        await repository.AddAsync(Entry("Quote", payload: "say \"hello\""), Ct);
        await repository.AddAsync(Entry("Other", payload: "abc"), Ct);

        (await Titles(repository, "%")).Should().Equal("Discount");
        (await Titles(repository, "a_b@")).Should().Equal("Mail");
        (await Titles(repository, "\"hello\"")).Should().Equal("Quote");
        (await Titles(repository, "@")).Should().Equal("Mail");
    }

    [Fact]
    public void Fts_expression_quotes_every_term()
    {
        SqliteHistoryRepository.BuildFtsMatchExpression("foo \"bar\" NEAR(x)").Should().Be("\"foo\"* \"\"\"bar\"\"\"* \"NEAR(x)\"*");
        SqliteHistoryRepository.BuildFtsMatchExpression("  %% ** ").Should().BeNull();
        SqliteHistoryRepository.EscapeLike(@"50%_\").Should().Be(@"50\%\_\\");
    }

    [Fact]
    public async Task Search_index_follows_updates_and_deletes()
    {
        var repository = Create();
        var entry = Entry("Original title", payload: "payload");
        await repository.AddAsync(entry, Ct);

        await repository.UpdateAsync(entry with { Title = "Renamed", Notes = "fresh note" }, Ct);
        (await Titles(repository, "original")).Should().BeEmpty();
        (await Titles(repository, "renamed")).Should().Equal("Renamed");
        (await Titles(repository, "fresh")).Should().Equal("Renamed");

        await repository.DeleteAsync(entry.Id, Ct);
        (await Titles(repository, "renamed")).Should().BeEmpty();
    }

    [Fact]
    public async Task Filters_by_kind_favourites_and_content_type()
    {
        var repository = Create();
        await repository.AddAsync(Entry("g-url", kind: HistoryKind.Generated, type: ContentType.Url, favorite: true), Ct);
        await repository.AddAsync(Entry("g-text", kind: HistoryKind.Generated, type: ContentType.Text), Ct);
        await repository.AddAsync(Entry("s-url", kind: HistoryKind.Scanned, type: ContentType.Url), Ct);
        await repository.AddAsync(Entry("s-wifi", kind: HistoryKind.Scanned, type: ContentType.Wifi, favorite: true), Ct);

        (await Titles(repository, new HistoryQuery(Kind: HistoryKind.Scanned, Sort: HistorySort.TitleAscending))).Should().Equal("s-url", "s-wifi");
        (await Titles(repository, new HistoryQuery(FavoritesOnly: true, Sort: HistorySort.TitleAscending))).Should().Equal("g-url", "s-wifi");
        (await Titles(repository, new HistoryQuery(ContentType: ContentType.Url, Sort: HistorySort.TitleAscending))).Should().Equal("g-url", "s-url");
        (await Titles(repository, new HistoryQuery(Kind: HistoryKind.Generated, FavoritesOnly: true, ContentType: ContentType.Url))).Should().Equal("g-url");
        (await Titles(repository, new HistoryQuery(Kind: HistoryKind.Scanned, Search: "wifi"))).Should().Equal("s-wifi");

        (await repository.CountAsync(HistoryKind.Generated, Ct)).Should().Be(2);
        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(4);
    }

    [Fact]
    public async Task Sorts_and_pages()
    {
        var repository = Create();
        for (var i = 0; i < 10; i++)
            await repository.AddAsync(Entry($"item-{i}", createdAt: T0.AddMinutes(i)), Ct);

        (await Titles(repository, new HistoryQuery(Take: 3))).Should().Equal("item-9", "item-8", "item-7");
        (await Titles(repository, new HistoryQuery(Skip: 3, Take: 3))).Should().Equal("item-6", "item-5", "item-4");
        (await Titles(repository, new HistoryQuery(Sort: HistorySort.OldestFirst, Skip: 8, Take: 5))).Should().Equal("item-8", "item-9");
        (await Titles(repository, new HistoryQuery(Take: 0))).Should().BeEmpty();
        (await Titles(repository, new HistoryQuery(Skip: 50))).Should().BeEmpty();
        (await repository.QueryAsync(new HistoryQuery(Take: int.MaxValue), Ct)).Should().HaveCount(10);
    }

    [Fact]
    public async Task Title_sort_is_case_insensitive()
    {
        var repository = Create();
        await repository.AddAsync(Entry("banana"), Ct);
        await repository.AddAsync(Entry("Apple"), Ct);
        await repository.AddAsync(Entry("cherry"), Ct);

        (await Titles(repository, new HistoryQuery(Sort: HistorySort.TitleAscending))).Should().Equal("Apple", "banana", "cherry");
    }

    [Fact]
    public async Task Prune_deletes_oldest_non_favourites()
    {
        var repository = Create();
        await repository.AddAsync(Entry("oldest-favourite", createdAt: T0, favorite: true), Ct);
        for (var i = 1; i <= 5; i++)
            await repository.AddAsync(Entry($"item-{i}", createdAt: T0.AddMinutes(i)), Ct);

        var deleted = await repository.PruneAsync(3, Ct);

        deleted.Should().Be(3);
        (await Titles(repository, new HistoryQuery(Sort: HistorySort.OldestFirst))).Should().Equal("oldest-favourite", "item-4", "item-5");
        (await repository.PruneAsync(3, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Prune_never_deletes_favourites_even_when_over_the_limit()
    {
        var repository = Create();
        for (var i = 0; i < 3; i++)
            await repository.AddAsync(Entry($"fav-{i}", createdAt: T0.AddMinutes(i), favorite: true), Ct);
        await repository.AddAsync(Entry("plain", createdAt: T0.AddMinutes(10)), Ct);

        (await repository.PruneAsync(1, Ct)).Should().Be(1);
        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(3);

        var invalid = () => repository.PruneAsync(-1, Ct);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Delete_all_by_kind_and_everything()
    {
        var repository = Create();
        await repository.AddAsync(Entry("generated", kind: HistoryKind.Generated), Ct);
        await repository.AddAsync(Entry("scanned", kind: HistoryKind.Scanned), Ct);

        await repository.DeleteAllAsync(HistoryKind.Scanned, Ct);
        (await Titles(repository, new HistoryQuery())).Should().Equal("generated");
        (await Titles(repository, "scanned")).Should().BeEmpty();

        await repository.DeleteAllAsync(cancellationToken: Ct);
        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_writes_and_reads_are_safe()
    {
        var repository = Create();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            await repository.AddAsync(Entry($"parallel-{i}", createdAt: T0.AddSeconds(i)), Ct);
            await repository.QueryAsync(new HistoryQuery(Search: "parallel"), Ct);
        }, Ct)));

        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(40);
        (await repository.QueryAsync(new HistoryQuery(Search: "parallel", Take: 100), Ct)).Should().HaveCount(40);
    }

    [Fact]
    public async Task Operations_initialise_lazily()
    {
        var repository = Create();

        (await repository.CountAsync(cancellationToken: Ct)).Should().Be(0);
        await repository.AddAsync(Entry("lazy"), Ct);
        (await repository.GetAsync(Guid.NewGuid(), Ct)).Should().BeNull();
    }

    private SqliteHistoryRepository Create(string? path = null, bool fullText = true) =>
        Track(new SqliteHistoryRepository(path ?? _temp.File($"{Guid.NewGuid():N}.db3"), fullText));

    private SqliteHistoryRepository Track(SqliteHistoryRepository repository)
    {
        _repositories.Add(repository);
        return repository;
    }

    private static async Task<IReadOnlyList<string>> Titles(SqliteHistoryRepository repository, string search) =>
        await Titles(repository, new HistoryQuery(Search: search, Sort: HistorySort.TitleAscending));

    private static async Task<IReadOnlyList<string>> Titles(SqliteHistoryRepository repository, HistoryQuery query) =>
        (await repository.QueryAsync(query, Ct)).Select(e => e.Title).ToList();

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static HistoryEntry Entry(
        string title,
        string? payload = null,
        HistoryKind kind = HistoryKind.Generated,
        ContentType type = ContentType.Text,
        bool favorite = false,
        DateTimeOffset? createdAt = null,
        string? notes = null,
        string? tags = null) => new()
    {
        Kind = kind,
        Payload = payload ?? title,
        ContentType = type,
        Title = title,
        CreatedAt = createdAt ?? T0,
        IsFavorite = favorite,
        Notes = notes,
        Tags = tags
    };
}
