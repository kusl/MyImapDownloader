using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// Manages the SQLite database for email search indexing.
/// Uses FTS5 for full-text search and B-tree indexes for structured queries.
/// </summary>
public sealed class SearchDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SearchDatabase> _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public string DatabasePath { get; }

    public SearchDatabase(string databasePath, ILogger<SearchDatabase> logger)
    {
        DatabasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    /// <summary>
    /// Initializes the database, creating tables if they don't exist.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing search database at {Path}", DatabasePath);

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        // Enable WAL mode for better concurrency
        await ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA mmap_size = 268435456;", ct).ConfigureAwait(false);

        // Create main emails table
        const string createEmailsTable = """
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id TEXT NOT NULL UNIQUE,
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
                indexed_at_unix INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );
            """;
        await ExecuteNonQueryAsync(createEmailsTable, ct).ConfigureAwait(false);

        // Create FTS5 virtual table for full-text search
        const string createFtsTable = """
            CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                subject,
                body_text,
                from_address,
                to_addresses,
                content='emails',
                content_rowid='id',
                tokenize='porter unicode61'
            );
            """;
        await ExecuteNonQueryAsync(createFtsTable, ct).ConfigureAwait(false);

        // Create triggers to keep FTS in sync
        const string createInsertTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createInsertTrigger, ct).ConfigureAwait(false);

        const string createDeleteTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createDeleteTrigger, ct).ConfigureAwait(false);

        const string createUpdateTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createUpdateTrigger, ct).ConfigureAwait(false);

        // Create indexes for structured queries
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address COLLATE NOCASE);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix DESC);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);", ct)
            .ConfigureAwait(false);

        // Create metadata table for tracking index state
        const string createMetadataTable = """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at_unix INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );
            """;
        await ExecuteNonQueryAsync(createMetadataTable, ct).ConfigureAwait(false);

        _logger.LogInformation("Search database initialized successfully");
    }

    /// <summary>
    /// Checks if the database is healthy and can be used.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureConnectionAsync(ct).ConfigureAwait(false);
            var result = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            return false;
        }
    }

    /// <summary>
    /// Inserts or updates an email document in the index.
    /// </summary>
    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name, to_addresses,
                cc_addresses, bcc_addresses, subject, date_sent_unix, date_received_unix,
                folder, account, has_attachments, attachment_names, body_preview, body_text
            ) VALUES (
                @message_id, @file_path, @from_address, @from_name, @to_addresses,
                @cc_addresses, @bcc_addresses, @subject, @date_sent_unix, @date_received_unix,
                @folder, @account, @has_attachments, @attachment_names, @body_preview, @body_text
            )
            ON CONFLICT(message_id) DO UPDATE SET
                file_path = excluded.file_path,
                from_address = excluded.from_address,
                from_name = excluded.from_name,
                to_addresses = excluded.to_addresses,
                cc_addresses = excluded.cc_addresses,
                bcc_addresses = excluded.bcc_addresses,
                subject = excluded.subject,
                date_sent_unix = excluded.date_sent_unix,
                date_received_unix = excluded.date_received_unix,
                folder = excluded.folder,
                account = excluded.account,
                has_attachments = excluded.has_attachments,
                attachment_names = excluded.attachment_names,
                body_preview = excluded.body_preview,
                body_text = excluded.body_text,
                indexed_at_unix = strftime('%s', 'now');
            """;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", email.MessageId);
        cmd.Parameters.AddWithValue("@file_path", email.FilePath);
        cmd.Parameters.AddWithValue("@from_address", (object?)email.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from_name", (object?)email.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to_addresses", (object?)email.ToAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cc_addresses", (object?)email.CcAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bcc_addresses", (object?)email.BccAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)email.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_sent_unix", email.DateSentUnix ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@date_received_unix", email.DateReceivedUnix ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", (object?)email.Folder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@account", (object?)email.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@has_attachments", email.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@attachment_names", (object?)email.AttachmentNamesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body_preview", (object?)email.BodyPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body_text", (object?)email.BodyText ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Batch inserts multiple emails efficiently within a transaction.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(
        IEnumerable<EmailDocument> emails,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var email in emails)
            {
                ct.ThrowIfCancellationRequested();
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
    /// Executes a full-text search query.
    /// </summary>
    public async Task<IReadOnlyList<EmailDocument>> SearchAsync(
        string ftsQuery,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.*, bm25(emails_fts) as rank
            FROM emails e
            JOIN emails_fts ON e.id = emails_fts.rowid
            WHERE emails_fts MATCH @query
            ORDER BY rank
            LIMIT @limit OFFSET @offset;
            """;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var results = new List<EmailDocument>();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", ftsQuery);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadEmailDocument(reader));
        }

        return results;
    }

    /// <summary>
    /// Executes a structured query with optional filters.
    /// </summary>
    public async Task<IReadOnlyList<EmailDocument>> QueryAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var (sql, parameters) = BuildStructuredQuery(query);
        var results = new List<EmailDocument>();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadEmailDocument(reader));
        }

        return results;
    }

    private (string Sql, List<(string Name, object Value)> Parameters) BuildStructuredQuery(SearchQuery query)
    {
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        var useFts = !string.IsNullOrWhiteSpace(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("from_address LIKE @from_address COLLATE NOCASE");
                parameters.Add(("@from_address", query.FromAddress.Replace('*', '%')));
            }
            else
            {
                conditions.Add("from_address = @from_address COLLATE NOCASE");
                parameters.Add(("@from_address", query.FromAddress));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            if (query.ToAddress.Contains('*'))
            {
                conditions.Add("to_addresses LIKE @to_address COLLATE NOCASE");
                parameters.Add(("@to_address", $"%{query.ToAddress.Replace('*', '%')}%"));
            }
            else
            {
                conditions.Add("to_addresses LIKE @to_address COLLATE NOCASE");
                parameters.Add(("@to_address", $"%{query.ToAddress}%"));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("subject LIKE @subject COLLATE NOCASE");
            parameters.Add(("@subject", $"%{query.Subject}%"));
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("date_sent_unix >= @date_from");
            parameters.Add(("@date_from", query.DateFrom.Value.ToUnixTimeSeconds()));
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("date_sent_unix <= @date_to");
            parameters.Add(("@date_to", query.DateTo.Value.ToUnixTimeSeconds()));
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("account = @account");
            parameters.Add(("@account", query.Account));
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("folder = @folder");
            parameters.Add(("@folder", query.Folder));
        }

        string sql;
        if (useFts)
        {
            // Combined FTS + structured query
            conditions.Add("emails_fts MATCH @fts_query");
            parameters.Add(("@fts_query", EscapeFtsQuery(query.ContentTerms!)));

            var whereClause = conditions.Count > 0
                ? $"WHERE {string.Join(" AND ", conditions)}"
                : "";

            var orderBy = query.SortOrder switch
            {
                SearchSortOrder.DateDescending => "ORDER BY e.date_sent_unix DESC",
                SearchSortOrder.DateAscending => "ORDER BY e.date_sent_unix ASC",
                SearchSortOrder.Relevance => "ORDER BY bm25(emails_fts)",
                _ => "ORDER BY e.date_sent_unix DESC"
            };

            sql = $"""
                SELECT e.*
                FROM emails e
                JOIN emails_fts ON e.id = emails_fts.rowid
                {whereClause}
                {orderBy}
                LIMIT @limit OFFSET @offset;
                """;
        }
        else
        {
            var whereClause = conditions.Count > 0
                ? $"WHERE {string.Join(" AND ", conditions)}"
                : "";

            var orderBy = query.SortOrder switch
            {
                SearchSortOrder.DateDescending => "ORDER BY date_sent_unix DESC",
                SearchSortOrder.DateAscending => "ORDER BY date_sent_unix ASC",
                _ => "ORDER BY date_sent_unix DESC"
            };

            sql = $"""
                SELECT *
                FROM emails
                {whereClause}
                {orderBy}
                LIMIT @limit OFFSET @offset;
                """;
        }

        parameters.Add(("@limit", query.Take));
        parameters.Add(("@offset", query.Skip));

        return (sql, parameters);
    }

    private static string EscapeFtsQuery(string input)
    {
        // Basic FTS5 query escaping
        return input
            .Replace("\"", "\"\"")
            .Replace("'", "''");
    }

    private static EmailDocument ReadEmailDocument(SqliteDataReader reader)
    {
        return new EmailDocument
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MessageId = reader.GetString(reader.GetOrdinal("message_id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            FromAddress = reader.IsDBNull(reader.GetOrdinal("from_address"))
                ? null : reader.GetString(reader.GetOrdinal("from_address")),
            FromName = reader.IsDBNull(reader.GetOrdinal("from_name"))
                ? null : reader.GetString(reader.GetOrdinal("from_name")),
            ToAddressesJson = reader.IsDBNull(reader.GetOrdinal("to_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("to_addresses")),
            CcAddressesJson = reader.IsDBNull(reader.GetOrdinal("cc_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("cc_addresses")),
            BccAddressesJson = reader.IsDBNull(reader.GetOrdinal("bcc_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("bcc_addresses")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject"))
                ? null : reader.GetString(reader.GetOrdinal("subject")),
            DateSentUnix = reader.IsDBNull(reader.GetOrdinal("date_sent_unix"))
                ? null : reader.GetInt64(reader.GetOrdinal("date_sent_unix")),
            DateReceivedUnix = reader.IsDBNull(reader.GetOrdinal("date_received_unix"))
                ? null : reader.GetInt64(reader.GetOrdinal("date_received_unix")),
            Folder = reader.IsDBNull(reader.GetOrdinal("folder"))
                ? null : reader.GetString(reader.GetOrdinal("folder")),
            Account = reader.IsDBNull(reader.GetOrdinal("account"))
                ? null : reader.GetString(reader.GetOrdinal("account")),
            HasAttachments = reader.GetInt32(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names"))
                ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview"))
                ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text"))
                ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix"))
        };
    }

    /// <summary>
    /// Gets the total count of indexed emails.
    /// </summary>
    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the database file size in bytes.
    /// </summary>
    public long GetDatabaseSize()
    {
        return File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
    }

    /// <summary>
    /// Gets metadata value by key.
    /// </summary>
    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        const string sql = "SELECT value FROM index_metadata WHERE key = @key;";
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string;
    }

    /// <summary>
    /// Sets metadata value by key.
    /// </summary>
    public async Task SetMetadataAsync(string key, string value, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO index_metadata (key, value, updated_at_unix)
            VALUES (@key, @value, strftime('%s', 'now'))
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_unix = strftime('%s', 'now');
            """;

        await ExecuteNonQueryAsync(sql, ct,
            ("@key", key),
            ("@value", value)).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if an email with the given message ID exists.
    /// </summary>
    public async Task<bool> EmailExistsAsync(string messageId, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM emails WHERE message_id = @message_id LIMIT 1;";
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", messageId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result != null;
    }

    /// <summary>
    /// Deletes all data and rebuilds the database schema.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding database - all indexed data will be lost");

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
        if (File.Exists(DatabasePath + "-wal"))
        {
            File.Delete(DatabasePath + "-wal");
        }
        if (File.Exists(DatabasePath + "-shm"))
        {
            File.Delete(DatabasePath + "-shm");
        }

        await InitializeAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken ct)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
