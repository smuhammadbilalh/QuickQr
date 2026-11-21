using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Core.Platform;

namespace QuickQr.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed history store (WAL journal, versioned migrations through <c>PRAGMA user_version</c>,
/// FTS5 full-text search with a LIKE fallback). Every statement is parameterised.
/// </summary>
/// <remarks>
/// Thread-safe: each operation opens its own pooled connection; writes are additionally serialised.
/// The FTS5 index is an external-content table keyed by the implicit <c>rowid</c>; if the database is ever
/// VACUUMed, run <c>INSERT INTO history_fts(history_fts) VALUES('rebuild')</c> afterwards.
/// </remarks>
public sealed class SqliteHistoryRepository : IHistoryRepository, IDisposable
{
    internal const string DatabaseFileName = "quickqr.db3";

    /// <summary>Schema migrations; index + 1 is the resulting <c>user_version</c>. Append only.</summary>
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE IF NOT EXISTS history (
            id           TEXT    NOT NULL PRIMARY KEY,
            kind         INTEGER NOT NULL,
            payload      TEXT    NOT NULL,
            content_type INTEGER NOT NULL,
            format       INTEGER NOT NULL,
            title        TEXT    NOT NULL,
            subtitle     TEXT    NULL,
            created_at   INTEGER NOT NULL,
            is_favorite  INTEGER NOT NULL DEFAULT 0,
            notes        TEXT    NULL,
            tags         TEXT    NULL,
            source       INTEGER NOT NULL DEFAULT 0,
            style_json   TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS ix_history_kind_created ON history (kind, created_at);
        CREATE INDEX IF NOT EXISTS ix_history_created ON history (created_at);
        CREATE INDEX IF NOT EXISTS ix_history_favorite ON history (is_favorite);
        """
    ];

    private const string FtsSchema =
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS history_fts USING fts5(
            title, subtitle, payload, notes, tags,
            content = 'history', content_rowid = 'rowid',
            tokenize = 'unicode61 remove_diacritics 2');
        CREATE TRIGGER IF NOT EXISTS history_fts_ai AFTER INSERT ON history BEGIN
            INSERT INTO history_fts (rowid, title, subtitle, payload, notes, tags)
            VALUES (new.rowid, new.title, new.subtitle, new.payload, new.notes, new.tags);
        END;
        CREATE TRIGGER IF NOT EXISTS history_fts_ad AFTER DELETE ON history BEGIN
            INSERT INTO history_fts (history_fts, rowid, title, subtitle, payload, notes, tags)
            VALUES ('delete', old.rowid, old.title, old.subtitle, old.payload, old.notes, old.tags);
        END;
        CREATE TRIGGER IF NOT EXISTS history_fts_au AFTER UPDATE ON history BEGIN
            INSERT INTO history_fts (history_fts, rowid, title, subtitle, payload, notes, tags)
            VALUES ('delete', old.rowid, old.title, old.subtitle, old.payload, old.notes, old.tags);
            INSERT INTO history_fts (rowid, title, subtitle, payload, notes, tags)
            VALUES (new.rowid, new.title, new.subtitle, new.payload, new.notes, new.tags);
        END;
        """;

    private const string SelectColumns =
        "h.id, h.kind, h.payload, h.content_type, h.format, h.title, h.subtitle, h.created_at, h.is_favorite, h.notes, h.tags, h.source, h.style_json";

    private readonly string _connectionString;
    private readonly bool _allowFullTextSearch;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private volatile bool _initialized;
    private bool _useFullTextSearch;

    public SqliteHistoryRepository(
        IOptions<QuickQrInfrastructureOptions> options,
        IAppInfoProvider appInfo,
        ILogger<SqliteHistoryRepository>? logger = null)
        : this(ResolvePath(options, appInfo), allowFullTextSearch: true, logger)
    {
    }

    /// <param name="allowFullTextSearch">False forces the LIKE fallback (used by tests).</param>
    internal SqliteHistoryRepository(string databasePath, bool allowFullTextSearch, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _allowFullTextSearch = allowFullTextSearch;
        _logger = logger ?? NullLogger.Instance;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 30
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>True once initialised with an FTS5 index; false when searching falls back to LIKE.</summary>
    public bool IsFullTextSearchEnabled => _useFullTextSearch;

    /// <summary>Current schema version (<c>PRAGMA user_version</c>) after initialisation.</summary>
    internal static int SchemaVersion => Migrations.Length;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
            await MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
            _useFullTextSearch = _allowFullTextSearch && await TryEnsureFullTextIndexAsync(connection, cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        const string sql =
            """
            INSERT INTO history (id, kind, payload, content_type, format, title, subtitle, created_at, is_favorite, notes, tags, source, style_json)
            VALUES ($id, $kind, $payload, $content_type, $format, $title, $subtitle, $created_at, $is_favorite, $notes, $tags, $source, $style_json)
            ON CONFLICT (id) DO UPDATE SET
                kind = excluded.kind, payload = excluded.payload, content_type = excluded.content_type, format = excluded.format,
                title = excluded.title, subtitle = excluded.subtitle, created_at = excluded.created_at, is_favorite = excluded.is_favorite,
                notes = excluded.notes, tags = excluded.tags, source = excluded.source, style_json = excluded.style_json;
            """;
        await WriteAsync(sql, command => BindEntry(command, entry), cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        const string sql =
            """
            UPDATE history SET
                kind = $kind, payload = $payload, content_type = $content_type, format = $format, title = $title,
                subtitle = $subtitle, created_at = $created_at, is_favorite = $is_favorite, notes = $notes, tags = $tags,
                source = $source, style_json = $style_json
            WHERE id = $id;
            """;
        await WriteAsync(sql, command => BindEntry(command, entry), cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM history h WHERE h.id = $id;";
        command.Parameters.AddWithValue("$id", FormatId(id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take <= 0) return [];
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var where = new List<string>();
        if (query.Kind is { } kind)
        {
            where.Add("h.kind = $kind");
            command.Parameters.AddWithValue("$kind", (int)kind);
        }

        if (query.FavoritesOnly) where.Add("h.is_favorite = 1");

        if (query.ContentType is { } contentType)
        {
            where.Add("h.content_type = $content_type");
            command.Parameters.AddWithValue("$content_type", (int)contentType);
        }

        AppendSearch(command, where, query.Search);

        var sql = new StringBuilder("SELECT ").Append(SelectColumns).Append(" FROM history h");
        if (where.Count > 0) sql.Append(" WHERE ").AppendJoin(" AND ", where);
        sql.Append(query.Sort switch
        {
            HistorySort.OldestFirst => " ORDER BY h.created_at ASC, h.rowid ASC",
            HistorySort.TitleAscending => " ORDER BY h.title COLLATE NOCASE ASC, h.created_at DESC, h.rowid DESC",
            _ => " ORDER BY h.created_at DESC, h.rowid DESC"
        });
        sql.Append(" LIMIT $take OFFSET $skip;");
        command.Parameters.AddWithValue("$take", query.Take);
        command.Parameters.AddWithValue("$skip", Math.Max(0, query.Skip));
        command.CommandText = sql.ToString();

        var results = new List<HistoryEntry>(Math.Min(query.Take, 256));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadEntry(reader));
        return results;
    }

    public async Task<int> CountAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (kind is { } k)
        {
            command.CommandText = "SELECT COUNT(*) FROM history WHERE kind = $kind;";
            command.Parameters.AddWithValue("$kind", (int)k);
        }
        else
        {
            command.CommandText = "SELECT COUNT(*) FROM history;";
        }

        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        WriteAsync("DELETE FROM history WHERE id = $id;", command => command.Parameters.AddWithValue("$id", FormatId(id)), cancellationToken);

    public Task DeleteAllAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default) =>
        kind is { } k
            ? WriteAsync("DELETE FROM history WHERE kind = $kind;", command => command.Parameters.AddWithValue("$kind", (int)k), cancellationToken)
            : WriteAsync("DELETE FROM history;", static _ => { }, cancellationToken);

    public async Task<int> PruneAsync(int keep, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            long total;
            await using (var count = connection.CreateCommand())
            {
                count.Transaction = transaction;
                count.CommandText = "SELECT COUNT(*) FROM history;";
                total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            var excess = total - keep;
            if (excess <= 0) return 0;

            int deleted;
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText =
                    """
                    DELETE FROM history WHERE rowid IN (
                        SELECT rowid FROM history WHERE is_favorite = 0
                        ORDER BY created_at ASC, rowid ASC
                        LIMIT $excess);
                    """;
                delete.Parameters.AddWithValue("$excess", excess);
                deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Releases pooled connections so the database file can be moved or deleted.</summary>
    public void Dispose()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>
    /// Builds an FTS5 MATCH expression: every word becomes a quoted prefix term (<c>"word"*</c>) so user input
    /// can never inject FTS syntax (<c>AND</c>, <c>NEAR</c>, <c>-</c>, <c>:</c>, parentheses, ...).
    /// Returns null when the text holds no indexable (letter/digit) characters.
    /// </summary>
    internal static string? BuildFtsMatchExpression(string search)
    {
        var terms = SplitTerms(search)
            .Where(static t => t.Any(char.IsLetterOrDigit))
            .Select(static t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*")
            .ToList();
        return terms.Count == 0 ? null : string.Join(' ', terms);
    }

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static string[] SplitTerms(string search) =>
        search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void AppendSearch(SqliteCommand command, List<string> where, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return;

        if (_useFullTextSearch && BuildFtsMatchExpression(search) is { } match)
        {
            where.Add("h.rowid IN (SELECT rowid FROM history_fts WHERE history_fts MATCH $match)");
            command.Parameters.AddWithValue("$match", match);
            return;
        }

        // LIKE fallback: every word must appear in at least one searchable column.
        var terms = SplitTerms(search);
        for (var i = 0; i < terms.Length; i++)
        {
            var name = "$term" + i.ToString(CultureInfo.InvariantCulture);
            where.Add(
                $"(h.title LIKE {name} ESCAPE '\\' OR h.subtitle LIKE {name} ESCAPE '\\' OR h.payload LIKE {name} ESCAPE '\\' " +
                $"OR h.notes LIKE {name} ESCAPE '\\' OR h.tags LIKE {name} ESCAPE '\\')");
            command.Parameters.AddWithValue(name, "%" + EscapeLike(terms[i]) + "%");
        }
    }

    private async Task WriteAsync(string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        for (var target = version + 1; target <= Migrations.Length; target++)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                // PRAGMA does not accept parameters; target is a trusted integer.
                migrate.CommandText = Migrations[target - 1] + $"\nPRAGMA user_version = {target.ToString(CultureInfo.InvariantCulture)};";
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryEnsureFullTextIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        bool exists;
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'history_fts';";
            exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
        }

        try
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = exists
                    ? FtsSchema
                    : FtsSchema + "\nINSERT INTO history_fts (history_fts) VALUES ('rebuild');";
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "FTS5 is not available; history search falls back to LIKE.");
            return false;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindEntry(SqliteCommand command, HistoryEntry entry)
    {
        command.Parameters.AddWithValue("$id", FormatId(entry.Id));
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$payload", entry.Payload ?? string.Empty);
        command.Parameters.AddWithValue("$content_type", (int)entry.ContentType);
        command.Parameters.AddWithValue("$format", (int)entry.Format);
        command.Parameters.AddWithValue("$title", entry.Title ?? string.Empty);
        command.Parameters.AddWithValue("$subtitle", (object?)entry.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$is_favorite", entry.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$tags", (object?)entry.Tags ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (int)entry.Source);
        command.Parameters.AddWithValue("$style_json", (object?)entry.StyleJson ?? DBNull.Value);
    }

    private static HistoryEntry ReadEntry(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Kind = (HistoryKind)reader.GetInt32(1),
        Payload = reader.GetString(2),
        ContentType = (ContentType)reader.GetInt32(3),
        Format = (BarcodeFormat)reader.GetInt32(4),
        Title = reader.GetString(5),
        Subtitle = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
        IsFavorite = reader.GetInt64(8) != 0,
        Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
        Tags = reader.IsDBNull(10) ? null : reader.GetString(10),
        Source = (ScanSource)reader.GetInt32(11),
        StyleJson = reader.IsDBNull(12) ? null : reader.GetString(12)
    };

    private static string FormatId(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string ResolvePath(IOptions<QuickQrInfrastructureOptions> options, IAppInfoProvider appInfo)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(appInfo);
        var configured = options.Value.DatabasePath;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(appInfo.AppDataDirectory, DatabaseFileName)
            : configured;
    }
}
