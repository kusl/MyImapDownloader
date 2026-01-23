using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// SQLite database for email search with FTS5 full-text search.
/// </summary>
public sealed partial class SearchDatabase(string databasePath, ILogger<SearchDatabase> logger) : IAsyncDisposable
{
    private readonly string _connectionString = $"Data Source={databasePath}";
    private readonly ILogger<SearchDatabase> _logger = logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    private string DatabasePath { get; } = databasePath;

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
                SELECT emails.*
                FROM emails
                INNER JOIN emails_fts ON emails.id = emails_fts.rowid
                WHERE emails_fts MATCH @ftsQuery {whereClause}
                ORDER BY bm25(emails_fts)
                LIMIT @limit OFFSET @offset;
                """;
            parameters["@ftsQuery"] = ftsQuery;
        }
        else
        {
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT * FROM emails
                {whereClause}
                ORDER BY date_sent_unix DESC
                LIMIT @limit OFFSET @offset;
                """;
        }

        parameters["@limit"] = query.Take;
        parameters["@offset"] = query.Skip;

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
    /// This is the fix for the TotalCount bug.
    /// </summary>
    public async Task<int> GetTotalCountForQueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
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

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static void AddQueryConditions(SearchQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("emails.from_address LIKE @fromAddress"); // Fixed: Added 'emails.' prefix
                parameters["@fromAddress"] = query.FromAddress.Replace('*', '%');
            }
            else
            {
                conditions.Add("emails.from_address = @fromAddress"); // Fixed: Added 'emails.' prefix
                parameters["@fromAddress"] = query.FromAddress;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            conditions.Add("emails.to_addresses LIKE @toAddress"); // Fixed: Added 'emails.' prefix
            parameters["@toAddress"] = $"%{query.ToAddress}%";
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("emails.subject LIKE @subject"); // Fixed: Added 'emails.' prefix
            parameters["@subject"] = $"%{query.Subject}%";
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("emails.date_sent_unix >= @dateFrom"); // Fixed: Added 'emails.' prefix
            parameters["@dateFrom"] = query.DateFrom.Value.ToUnixTimeSeconds();
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("emails.date_sent_unix <= @dateTo"); // Fixed: Added 'emails.' prefix
            parameters["@dateTo"] = query.DateTo.Value.ToUnixTimeSeconds();
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("emails.account = @account"); // Fixed: Added 'emails.' prefix
            parameters["@account"] = query.Account;
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("emails.folder = @folder"); // Fixed: Added 'emails.' prefix
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

    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
    }

    public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await GetEmailCountAsync(ct).ConfigureAwait(false);
    }

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

    public async Task<DatabaseStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var totalCount = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
        var headerCount = totalCount;
        var contentCount = await ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM emails WHERE body_text IS NOT NULL AND body_text != '';", ct).ConfigureAwait(false);

        long ftsSize = 0;
        try
        {
            var pageCount = await ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM emails_fts_data;", ct).ConfigureAwait(false);
            ftsSize = pageCount * 4096;
        }
        catch { /* FTS tables might not have _data table accessible */ }

        var accountCounts = new Dictionary<string, long>();
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

        var folderCounts = new Dictionary<string, long>();
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
            TotalEmails = totalCount,
            HeaderIndexed = headerCount,
            ContentIndexed = contentCount,
            FtsIndexSize = ftsSize,
            AccountCounts = accountCounts,
            FolderCounts = folderCounts
        };
    }

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

    public async Task ClearAllDataAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

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
            DateSentUnix = reader.IsDBNull(reader.GetOrdinal("date_sent_unix")) ? null : reader.GetInt64(reader.GetOrdinal("date_sent_unix")),
            DateReceivedUnix = reader.IsDBNull(reader.GetOrdinal("date_received_unix")) ? null : reader.GetInt64(reader.GetOrdinal("date_received_unix")),
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

    public long GetDatabaseSize()
    {
        if (!File.Exists(DatabasePath)) return 0;
        return new FileInfo(DatabasePath).Length;
    }
}
