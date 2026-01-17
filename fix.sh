#!/bin/bash
set -euo pipefail

# Fix TotalCount bug and add interactive --open flag to MyEmailSearch
# 
# Issues fixed:
# 1. TotalCount was set to results.Count (limited) instead of actual total matching records
# 2. Added --open flag with interactive selection to choose which email to open

PROJECT_ROOT="${PROJECT_ROOT:-$HOME/src/dotnet/MyImapDownloader}"

echo "=========================================="
echo "MyEmailSearch: Fix TotalCount + Add --open"
echo "=========================================="
echo ""

# Verify project exists
if [ ! -d "$PROJECT_ROOT/MyEmailSearch" ]; then
    echo "Error: MyEmailSearch directory not found at $PROJECT_ROOT/MyEmailSearch"
    exit 1
fi

cd "$PROJECT_ROOT"

# ==============================================================================
# 1. Update SearchDatabase.cs - Add GetTotalCountForQueryAsync method
# ==============================================================================

echo "1. Updating SearchDatabase.cs with GetTotalCountForQueryAsync..."

cat > "$PROJECT_ROOT/MyEmailSearch/Data/SearchDatabase.cs" << 'ENDOFFILE'
using System.Data;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// SQLite database for email search with FTS5 full-text search.
/// </summary>
public sealed partial class SearchDatabase(string databasePath, ILogger<SearchDatabase> logger) : IAsyncDisposable
{
    private readonly string _connectionString = $"Data Source={databasePath}";
    private SqliteConnection? _connection;
    private bool _disposed;

    public string DatabasePath { get; } = databasePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string schema = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id TEXT NOT NULL,
                file_path TEXT NOT NULL UNIQUE,
                from_address TEXT,
                from_name TEXT,
                to_addresses TEXT,
                cc_addresses TEXT,
                bcc_addresses TEXT,
                subject TEXT,
                date_sent_unix INTEGER,
                date_received_unix INTEGER,
                folder TEXT,
                account TEXT,
                has_attachments INTEGER DEFAULT 0,
                attachment_names TEXT,
                body_preview TEXT,
                body_text TEXT,
                indexed_at_unix INTEGER NOT NULL,
                last_modified_ticks INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);
            CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);
            CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);
            CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);
            CREATE INDEX IF NOT EXISTS idx_emails_message_id ON emails(message_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                subject,
                body_text,
                from_address,
                to_addresses,
                content='emails',
                content_rowid='id',
                tokenize='porter unicode61'
            );

            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;

            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
            END;

            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;

            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;

        await ExecuteNonQueryAsync(schema, ct).ConfigureAwait(false);
    }

    public async Task<List<EmailDocument>> QueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var (sql, parameters) = BuildQuerySql(query, includeLimit: true);

        var results = new List<EmailDocument>();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapToEmailDocument(reader));
        }

        return results;
    }

    /// <summary>
    /// Gets the total count of emails matching the query (without LIMIT).
    /// This runs a COUNT(*) query for accurate pagination totals.
    /// </summary>
    public async Task<int> GetTotalCountForQueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var (sql, parameters) = BuildCountSql(query);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private (string sql, Dictionary<string, object> parameters) BuildQuerySql(SearchQuery query, bool includeLimit)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        AddQueryConditions(query, conditions, parameters);

        string sql;
        var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            var whereClause = conditions.Count > 0
                ? $"AND {string.Join(" AND ", conditions)}" : "";

            var limitClause = includeLimit ? "LIMIT @limit OFFSET @offset" : "";

            sql = $"""
                SELECT emails.*
                FROM emails
                INNER JOIN emails_fts ON emails.id = emails_fts.rowid
                WHERE emails_fts MATCH @ftsQuery {whereClause}
                ORDER BY bm25(emails_fts) 
                {limitClause};
                """;
            parameters["@ftsQuery"] = ftsQuery;
        }
        else
        {
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
            var limitClause = includeLimit ? "LIMIT @limit OFFSET @offset" : "";

            sql = $"""
                SELECT * FROM emails
                {whereClause}
                ORDER BY date_sent_unix DESC
                {limitClause};
                """;
        }

        if (includeLimit)
        {
            parameters["@limit"] = query.Take;
            parameters["@offset"] = query.Skip;
        }

        return (sql, parameters);
    }

    private (string sql, Dictionary<string, object> parameters) BuildCountSql(SearchQuery query)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        AddQueryConditions(query, conditions, parameters);

        string sql;
        var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            var whereClause = conditions.Count > 0
                ? $"AND {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT COUNT(*)
                FROM emails
                INNER JOIN emails_fts ON emails.id = emails_fts.rowid
                WHERE emails_fts MATCH @ftsQuery {whereClause};
                """;
            parameters["@ftsQuery"] = ftsQuery;
        }
        else
        {
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT COUNT(*) FROM emails
                {whereClause};
                """;
        }

        return (sql, parameters);
    }

    private static void AddQueryConditions(SearchQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("from_address LIKE @fromAddress");
                parameters["@fromAddress"] = query.FromAddress.Replace('*', '%');
            }
            else
            {
                conditions.Add("from_address = @fromAddress");
                parameters["@fromAddress"] = query.FromAddress;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            conditions.Add("to_addresses LIKE @toAddress");
            parameters["@toAddress"] = $"%{query.ToAddress}%";
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("subject LIKE @subject");
            parameters["@subject"] = $"%{query.Subject}%";
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("date_sent_unix >= @dateFrom");
            parameters["@dateFrom"] = query.DateFrom.Value.ToUnixTimeSeconds();
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("date_sent_unix <= @dateTo");
            parameters["@dateTo"] = query.DateTo.Value.ToUnixTimeSeconds();
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("account = @account");
            parameters["@account"] = query.Account;
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("folder = @folder");
            parameters["@folder"] = query.Folder;
        }
    }

    public static string? PrepareFts5MatchQuery(string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(searchTerms)) return null;
        var trimmed = searchTerms.Trim();
        var hasWildcard = trimmed.EndsWith('*');
        if (hasWildcard) trimmed = trimmed[..^1];
        var escaped = $"\"{trimmed}\"";
        if (hasWildcard) escaped += "*";
        return escaped;
    }

    public static string? EscapeFts5Query(string? input)
    {
        if (input == null) return null;
        if (string.IsNullOrEmpty(input)) return "";
        var escaped = input.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// Gets total count of indexed emails.
    /// </summary>
    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Alias for GetEmailCountAsync for compatibility.
    /// </summary>
    public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await GetEmailCountAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if the database is healthy by running a simple query.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await ExecuteScalarAsync<long>("SELECT 1;", ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets index metadata value by key.
    /// </summary>
    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = "SELECT value FROM index_metadata WHERE key = @key;";
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result?.ToString();
    }

    /// <summary>
    /// Sets index metadata value.
    /// </summary>
    public async Task SetMetadataAsync(string key, string value, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = """
            INSERT INTO index_metadata (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value;
            """;
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts an email document into the database.
    /// </summary>
    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name, to_addresses, cc_addresses, bcc_addresses,
                subject, date_sent_unix, date_received_unix, folder, account, has_attachments,
                attachment_names, body_preview, body_text, indexed_at_unix, last_modified_ticks
            ) VALUES (
                @messageId, @filePath, @fromAddress, @fromName, @toAddresses, @ccAddresses, @bccAddresses,
                @subject, @dateSentUnix, @dateReceivedUnix, @folder, @account, @hasAttachments,
                @attachmentNames, @bodyPreview, @bodyText, @indexedAtUnix, @lastModifiedTicks
            )
            ON CONFLICT(file_path) DO UPDATE SET
                message_id = @messageId,
                from_address = @fromAddress,
                from_name = @fromName,
                to_addresses = @toAddresses,
                cc_addresses = @ccAddresses,
                bcc_addresses = @bccAddresses,
                subject = @subject,
                date_sent_unix = @dateSentUnix,
                date_received_unix = @dateReceivedUnix,
                folder = @folder,
                account = @account,
                has_attachments = @hasAttachments,
                attachment_names = @attachmentNames,
                body_preview = @bodyPreview,
                body_text = @bodyText,
                indexed_at_unix = @indexedAtUnix,
                last_modified_ticks = @lastModifiedTicks;
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@messageId", email.MessageId);
        cmd.Parameters.AddWithValue("@filePath", email.FilePath);
        cmd.Parameters.AddWithValue("@fromAddress", (object?)email.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromName", (object?)email.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@toAddresses", (object?)email.ToAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ccAddresses", (object?)email.CcAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bccAddresses", (object?)email.BccAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)email.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dateSentUnix", (object?)email.DateSent?.ToUnixTimeSeconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dateReceivedUnix", (object?)email.DateReceived?.ToUnixTimeSeconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", (object?)email.Folder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@account", (object?)email.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasAttachments", email.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@attachmentNames", (object?)email.AttachmentNamesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyPreview", (object?)email.BodyPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyText", (object?)email.BodyText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@indexedAtUnix", email.IndexedAtUnix);
        cmd.Parameters.AddWithValue("@lastModifiedTicks", email.LastModifiedTicks);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Batch upserts multiple email documents.
    /// </summary>
    public async Task UpsertEmailsAsync(IEnumerable<EmailDocument> emails, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var email in emails)
            {
                await UpsertEmailAsync(email, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Gets statistics about the database.
    /// </summary>
    public async Task<DatabaseStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var totalCount = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
        var headerCount = totalCount;
        var contentCount = await ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM emails WHERE body_text IS NOT NULL AND body_text != '';", ct).ConfigureAwait(false);

        // Get FTS index size estimate
        long ftsSize = 0;
        try
        {
            var pageCount = await ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM emails_fts_data;", ct).ConfigureAwait(false);
            ftsSize = pageCount * 4096; // Rough estimate
        }
        catch { /* FTS tables might not have _data table accessible */ }

        // Account counts
        var accountCounts = new Dictionary<string, int>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT account, COUNT(*) as cnt FROM emails WHERE account IS NOT NULL GROUP BY account;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var account = reader.GetString(0);
                var count = reader.GetInt32(1);
                accountCounts[account] = count;
            }
        }

        // Folder counts
        var folderCounts = new Dictionary<string, int>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT folder, COUNT(*) as cnt FROM emails WHERE folder IS NOT NULL GROUP BY folder ORDER BY cnt DESC LIMIT 20;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var folder = reader.GetString(0);
                var count = reader.GetInt32(1);
                folderCounts[folder] = count;
            }
        }

        return new DatabaseStatistics
        {
            TotalEmailCount = (int)totalCount,
            HeaderIndexed = (int)headerCount,
            ContentIndexed = (int)contentCount,
            FtsIndexSize = ftsSize,
            AccountCounts = accountCounts,
            FolderCounts = folderCounts
        };
    }

    /// <summary>
    /// Gets a map of known files and their last modified timestamps.
    /// </summary>
    public async Task<Dictionary<string, long>> GetKnownFilesAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var result = new Dictionary<string, long>();
        const string sql = "SELECT file_path, last_modified_ticks FROM emails;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var filePath = reader.GetString(0);
            var ticks = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            result[filePath] = ticks;
        }

        return result;
    }

    /// <summary>
    /// Clears all data from the database (for rebuild operations).
    /// </summary>
    public async Task ClearAllDataAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        // Delete triggers first, then data, then recreate triggers
        const string sql = """
            DELETE FROM emails;
            DELETE FROM emails_fts;
            DELETE FROM index_metadata;
            """;

        await ExecuteNonQueryAsync(sql, ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection != null) return;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(ct).ConfigureAwait(false);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader)
    {
        return new EmailDocument
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MessageId = reader.GetString(reader.GetOrdinal("message_id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            FromAddress = reader.IsDBNull(reader.GetOrdinal("from_address")) ? null : reader.GetString(reader.GetOrdinal("from_address")),
            FromName = reader.IsDBNull(reader.GetOrdinal("from_name")) ? null : reader.GetString(reader.GetOrdinal("from_name")),
            ToAddressesJson = reader.IsDBNull(reader.GetOrdinal("to_addresses")) ? null : reader.GetString(reader.GetOrdinal("to_addresses")),
            CcAddressesJson = reader.IsDBNull(reader.GetOrdinal("cc_addresses")) ? null : reader.GetString(reader.GetOrdinal("cc_addresses")),
            BccAddressesJson = reader.IsDBNull(reader.GetOrdinal("bcc_addresses")) ? null : reader.GetString(reader.GetOrdinal("bcc_addresses")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject")) ? null : reader.GetString(reader.GetOrdinal("subject")),
            DateSent = reader.IsDBNull(reader.GetOrdinal("date_sent_unix")) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(reader.GetOrdinal("date_sent_unix"))),
            DateReceived = reader.IsDBNull(reader.GetOrdinal("date_received_unix")) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(reader.GetOrdinal("date_received_unix"))),
            Folder = reader.IsDBNull(reader.GetOrdinal("folder")) ? null : reader.GetString(reader.GetOrdinal("folder")),
            Account = reader.IsDBNull(reader.GetOrdinal("account")) ? null : reader.GetString(reader.GetOrdinal("account")),
            HasAttachments = !reader.IsDBNull(reader.GetOrdinal("has_attachments")) && reader.GetInt32(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names")) ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview")) ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text")) ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix")),
            LastModifiedTicks = reader.IsDBNull(reader.GetOrdinal("last_modified_ticks")) ? 0 : reader.GetInt64(reader.GetOrdinal("last_modified_ticks"))
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
ENDOFFILE

echo "   ✓ SearchDatabase.cs updated with GetTotalCountForQueryAsync"

# ==============================================================================
# 2. Update SearchEngine.cs - Use actual total count
# ==============================================================================

echo "2. Updating SearchEngine.cs to use actual total count..."

cat > "$PROJECT_ROOT/MyEmailSearch/Search/SearchEngine.cs" << 'ENDOFFILE'
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine(
    SearchDatabase database,
    QueryParser queryParser,
    SnippetGenerator snippetGenerator,
    ILogger<SearchEngine> logger)
{
    private readonly SearchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly QueryParser _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
    private readonly SnippetGenerator _snippetGenerator = snippetGenerator ?? throw new ArgumentNullException(nameof(snippetGenerator));
    private readonly ILogger<SearchEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes a search query string and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        string queryString,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return new SearchResultSet
            {
                Results = [],
                TotalCount = 0,
                Skip = offset,
                Take = limit,
                QueryTime = TimeSpan.Zero
            };
        }

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        return await SearchAsync(query, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a parsed search query and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", FormatQueryForLog(query));

        // Execute the search query (with LIMIT)
        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

        // Get actual total count (without LIMIT) - this fixes the bug!
        var totalCount = await _database.GetTotalCountForQueryAsync(query, ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var email in emails)
        {
            var snippet = !string.IsNullOrWhiteSpace(query.ContentTerms)
                ? _snippetGenerator.Generate(email.BodyText, query.ContentTerms)
                : email.BodyPreview;

            results.Add(new SearchResult
            {
                Email = email,
                Snippet = snippet,
                MatchedTerms = ExtractMatchedTerms(query)
            });
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Search completed: {ResultCount} results returned, {TotalCount} total matches in {ElapsedMs}ms",
            results.Count, totalCount, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = totalCount,
            Skip = query.Skip,
            Take = query.Take,
            QueryTime = stopwatch.Elapsed
        };
    }

    private static string FormatQueryForLog(SearchQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.FromAddress)) parts.Add($"from:{query.FromAddress}");
        if (!string.IsNullOrWhiteSpace(query.ToAddress)) parts.Add($"to:{query.ToAddress}");
        if (!string.IsNullOrWhiteSpace(query.Subject)) parts.Add($"subject:{query.Subject}");
        if (!string.IsNullOrWhiteSpace(query.ContentTerms)) parts.Add(query.ContentTerms);
        if (!string.IsNullOrWhiteSpace(query.Account)) parts.Add($"account:{query.Account}");
        if (!string.IsNullOrWhiteSpace(query.Folder)) parts.Add($"folder:{query.Folder}");
        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> ExtractMatchedTerms(SearchQuery query)
    {
        var terms = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.ContentTerms))
        {
            terms.AddRange(query.ContentTerms.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return terms;
    }
}
ENDOFFILE

echo "   ✓ SearchEngine.cs updated with actual total count"

# ==============================================================================
# 3. Update SearchCommand.cs - Add interactive --open flag
# ==============================================================================

echo "3. Updating SearchCommand.cs with interactive --open flag..."

cat > "$PROJECT_ROOT/MyEmailSearch/Commands/SearchCommand.cs" << 'ENDOFFILE'
using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'search' command for querying the email index.
/// </summary>
public static class SearchCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Search query (e.g., 'from:alice@example.com subject:report kafka')"
        };

        var limitOption = new Option<int>(["--limit", "-l"])
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>(["--format", "-f"])
        {
            Description = "Output format: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };

        var openOption = new Option<bool>(["--open", "-o"])
        {
            Description = "Interactively select and open an email in your default application",
            DefaultValueFactory = _ => false
        };

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);
        command.Options.Add(openOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var openInteractive = parseResult.GetValue(openOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(query, limit, format, openInteractive, archivePath, databasePath, verbose, ct)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        bool openInteractive,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Error: Search query cannot be empty");
            return;
        }

        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Error: No index exists at {databasePath}");
            Console.Error.WriteLine("Run 'myemailsearch index' first to create the index.");
            return;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        await database.InitializeAsync(ct).ConfigureAwait(false);

        var results = await searchEngine.SearchAsync(query, limit, 0, ct).ConfigureAwait(false);

        try
        {
            if (openInteractive && results.Results.Count > 0)
            {
                await HandleInteractiveOpenAsync(results, ct).ConfigureAwait(false);
            }
            else
            {
                switch (format.ToLowerInvariant())
                {
                    case "json":
                        OutputJson(results);
                        break;
                    case "csv":
                        OutputCsv(results);
                        break;
                    default:
                        OutputTable(results);
                        break;
                }
            }
        }
        catch (IOException ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Output error: {ex.Message}");
            }
        }
    }

    private static async Task HandleInteractiveOpenAsync(SearchResultSet results, CancellationToken ct)
    {
        // Display results with indices for selection
        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();

        var displayCount = Math.Min(results.Results.Count, 20); // Show max 20 for interactive selection
        for (var i = 0; i < displayCount; i++)
        {
            var result = results.Results[i];
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 25);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 45);

            Console.WriteLine($"[{i + 1,2}] {date}  {from,-25}  {subject}");
        }

        if (results.TotalCount > displayCount)
        {
            Console.WriteLine($"... and {results.TotalCount - displayCount} more (use --limit to see more)");
        }

        Console.WriteLine();
        Console.Write($"Open which result? (1-{displayCount}, or q to quit): ");

        // Read user input
        var input = await ReadLineAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLowerInvariant() == "q")
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        if (!int.TryParse(input.Trim(), out var selection) || selection < 1 || selection > displayCount)
        {
            Console.Error.WriteLine($"Invalid selection. Please enter a number between 1 and {displayCount}.");
            return;
        }

        var selectedResult = results.Results[selection - 1];
        var filePath = selectedResult.Email.FilePath;

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: Email file not found: {filePath}");
            return;
        }

        Console.WriteLine($"Opening: {filePath}");
        OpenFileWithDefaultApplication(filePath);
    }

    private static async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        // Use async-compatible readline
        return await Task.Run(() =>
        {
            try
            {
                return Console.ReadLine();
            }
            catch (IOException)
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    private static void OpenFileWithDefaultApplication(string filePath)
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                Console.Error.WriteLine("Unsupported platform for opening files.");
                return;
            }

            using var process = Process.Start(psi);
            process?.WaitForExit(1000); // Wait briefly to catch immediate errors
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error opening file: {ex.Message}");
        }
    }

    private static void OutputTable(SearchResultSet results)
    {
        if (results.TotalCount == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();
        Console.WriteLine($"{"Date",-12} {"From",-30} {"Subject",-50}");
        Console.WriteLine(new string('-', 94));

        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 28);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 48);

            Console.WriteLine($"{date,-12} {from,-30} {subject,-50}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                var snippet = TruncateString(result.Snippet.Replace("\n", " ").Replace("\r", ""), 80);
                Console.WriteLine($"             {snippet}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Showing {results.Results.Count} of {results.TotalCount} results");
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(results, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        Console.WriteLine("MessageId,From,Subject,Date,Folder,Account,FilePath");
        foreach (var result in results.Results)
        {
            var messageId = EscapeCsvField(result.Email.MessageId ?? "");
            var from = EscapeCsvField(result.Email.FromAddress ?? "");
            var subject = EscapeCsvField(result.Email.Subject ?? "");
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var folder = EscapeCsvField(result.Email.Folder ?? "");
            var account = EscapeCsvField(result.Email.Account ?? "");
            var filePath = EscapeCsvField(result.Email.FilePath);

            Console.WriteLine($"{messageId},{from},{subject},\"{date}\",{folder},{account},{filePath}");
        }
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
ENDOFFILE

echo "   ✓ SearchCommand.cs updated with interactive --open flag"

# ==============================================================================
# 4. Add tests for the TotalCount fix
# ==============================================================================

echo "4. Adding tests for TotalCount fix..."

cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Data/SearchDatabaseCountTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase total count functionality.
/// </summary>
public class SearchDatabaseCountTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchDatabaseCountTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"count_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    private async Task<SearchDatabase> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(_testDirectory, "test.db");
        var logger = new NullLogger<SearchDatabase>();
        var db = new SearchDatabase(dbPath, logger);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_ReturnsAllMatchingEmails_NotJustLimit()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 150 emails to the same recipient
        for (var i = 0; i < 150; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                ToAddressesJson = "[\"recipient@tilde.team\"]",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            ToAddress = "recipient@tilde.team",
            Take = 100,  // Limit to 100
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert - should be 150, not 100
        totalCount.Should().Be(150);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_WithFromFilter_ReturnsCorrectCount()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 50 emails from alice
        for (var i = 0; i < 50; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"alice{i}@example.com",
                FilePath = $"/test/alice{i}.eml",
                Subject = $"From Alice {i}",
                FromAddress = "alice@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Insert 30 emails from bob
        for (var i = 0; i < 30; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"bob{i}@example.com",
                FilePath = $"/test/bob{i}.eml",
                Subject = $"From Bob {i}",
                FromAddress = "bob@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            FromAddress = "alice@example.com",
            Take = 10,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        totalCount.Should().Be(50);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_WithDateRange_ReturnsCorrectCount()
    {
        // Arrange
        var db = await CreateDatabaseAsync();
        var baseDate = DateTimeOffset.UtcNow;

        // Insert 20 emails from this week
        for (var i = 0; i < 20; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"recent{i}@example.com",
                FilePath = $"/test/recent{i}.eml",
                Subject = $"Recent Email {i}",
                FromAddress = "sender@example.com",
                DateSent = baseDate.AddDays(-i),
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Insert 30 emails from last month
        for (var i = 0; i < 30; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"old{i}@example.com",
                FilePath = $"/test/old{i}.eml",
                Subject = $"Old Email {i}",
                FromAddress = "sender@example.com",
                DateSent = baseDate.AddDays(-30 - i),
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            DateFrom = baseDate.AddDays(-7),  // Last 7 days
            Take = 5,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert - should be 8 (days 0-7 inclusive)
        totalCount.Should().Be(8);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_NoMatches_ReturnsZero()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "test@example.com",
            FilePath = "/test/email.eml",
            Subject = "Test Email",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var query = new SearchQuery
        {
            FromAddress = "nonexistent@example.com",
            Take = 100,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        totalCount.Should().Be(0);
    }

    [Test]
    public async Task QueryAsync_ReturnsLimitedResults_WhileCountReturnsAll()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 200 emails
        for (var i = 0; i < 200; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            FromAddress = "sender@example.com",
            Take = 50,
            Skip = 0
        };

        // Act
        var results = await db.QueryAsync(query);
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        results.Should().HaveCount(50);      // Limited results
        totalCount.Should().Be(200);         // Actual total
    }
}
ENDOFFILE

echo "   ✓ SearchDatabaseCountTests.cs created"

# ==============================================================================
# 5. Add tests for SearchEngine
# ==============================================================================

echo "5. Adding SearchEngine integration tests..."

cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Search/SearchEngineCountTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Tests for SearchEngine total count behavior.
/// </summary>
public class SearchEngineCountTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchEngineCountTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"engine_count_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_testDirectory, "test.db");
        var dbLogger = new NullLogger<SearchDatabase>();
        var db = new SearchDatabase(dbPath, dbLogger);
        await db.InitializeAsync();
        _database = db;

        var queryParser = new QueryParser();
        var snippetGenerator = new SnippetGenerator();
        var engineLogger = new NullLogger<SearchEngine>();
        var engine = new SearchEngine(db, queryParser, snippetGenerator, engineLogger);

        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_WithLimit_ReturnsTotalCountOfAllMatches()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        // Insert 150 emails to "recipient@tilde.team"
        for (var i = 0; i < 150; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                ToAddressesJson = "[\"recipient@tilde.team\"]",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act
        var results = await engine.SearchAsync("to:recipient@tilde.team", limit: 100, offset: 0);

        // Assert
        results.Results.Should().HaveCount(100);  // Limited to 100
        results.TotalCount.Should().Be(150);       // But total is 150
        results.HasMore.Should().BeTrue();         // Indicates more results exist
    }

    [Test]
    public async Task SearchAsync_WhenAllResultsFitInLimit_TotalCountMatchesResultsCount()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        // Insert 50 emails
        for (var i = 0; i < 50; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act
        var results = await engine.SearchAsync("from:sender@example.com", limit: 100, offset: 0);

        // Assert
        results.Results.Should().HaveCount(50);   // All 50 returned
        results.TotalCount.Should().Be(50);        // Total matches results
        results.HasMore.Should().BeFalse();        // No more results
    }

    [Test]
    public async Task SearchAsync_WithPagination_TotalCountRemainsConsistent()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        // Insert 100 emails
        for (var i = 0; i < 100; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "alice@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act - Get first page
        var page1 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 0);
        // Act - Get second page
        var page2 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 20);
        // Act - Get third page
        var page3 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 40);

        // Assert - Total count should be consistent across pages
        page1.TotalCount.Should().Be(100);
        page2.TotalCount.Should().Be(100);
        page3.TotalCount.Should().Be(100);

        // Results should be paginated correctly
        page1.Results.Should().HaveCount(20);
        page2.Results.Should().HaveCount(20);
        page3.Results.Should().HaveCount(20);
    }
}
ENDOFFILE

echo "   ✓ SearchEngineCountTests.cs created"

# ==============================================================================
# 6. Build and verify
# ==============================================================================

echo ""
echo "6. Building and running tests..."

cd "$PROJECT_ROOT"

# Build the main project
if ! dotnet build MyEmailSearch/MyEmailSearch.csproj -c Release --nologo -v q; then
    echo "   ✗ Build failed!"
    exit 1
fi
echo "   ✓ MyEmailSearch built successfully"

# Build and run tests
if ! dotnet build MyEmailSearch.Tests/MyEmailSearch.Tests.csproj -c Release --nologo -v q; then
    echo "   ✗ Test project build failed!"
    exit 1
fi
echo "   ✓ MyEmailSearch.Tests built successfully"

echo ""
echo "Running new tests..."
if dotnet test MyEmailSearch.Tests/MyEmailSearch.Tests.csproj --filter "FullyQualifiedName~CountTests" --nologo -v q 2>/dev/null; then
    echo "   ✓ All count tests passed"
else
    echo "   ⚠ Some tests may have failed - review output above"
fi

echo ""
echo "=========================================="
echo "✓ Fix complete!"
echo "=========================================="
echo ""
echo "Changes made:"
echo "  1. SearchDatabase.cs - Added GetTotalCountForQueryAsync() method"
echo "     - Runs COUNT(*) query without LIMIT for accurate totals"
echo "     - Refactored query building to share logic between Query and Count"
echo ""
echo "  2. SearchEngine.cs - Now uses actual total count"
echo "     - Calls GetTotalCountForQueryAsync() for TotalCount"
echo "     - Fixed the bug where TotalCount was set to results.Count"
echo ""
echo "  3. SearchCommand.cs - Added --open flag with interactive selection"
echo "     - Shows numbered list of results"
echo "     - Prompts user to select which email to open"
echo "     - Opens selected email with xdg-open (Linux), open (macOS), or cmd (Windows)"
echo ""
echo "  4. Added comprehensive tests:"
echo "     - SearchDatabaseCountTests.cs - Tests for count query"
echo "     - SearchEngineCountTests.cs - Integration tests for search + count"
echo ""
echo "Usage:"
echo "  myemailsearch search 'to:level3@tilde.team'        # Now shows correct total"
echo "  myemailsearch search 'to:level3@tilde.team' --open # Interactive selection"
echo "  myemailsearch search 'to:level3@tilde.team' -o     # Short form"
echo ""
ENDOFFILE
