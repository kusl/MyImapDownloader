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
                message_id,
                from_address,
                from_name,
                to_addresses,
                subject,
                body_text,
                content='emails',
                content_rowid='id',
                tokenize='porter unicode61'
            );
            """;
        await ExecuteNonQueryAsync(createFtsTable, ct).ConfigureAwait(false);

        // Create triggers to keep FTS in sync
        const string createInsertTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(rowid, message_id, from_address, from_name, to_addresses, subject, body_text)
                VALUES (new.id, new.message_id, new.from_address, new.from_name, new.to_addresses, new.subject, new.body_text);
            END;
            """;
        await ExecuteNonQueryAsync(createInsertTrigger, ct).ConfigureAwait(false);

        const string createDeleteTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, message_id, from_address, from_name, to_addresses, subject, body_text)
                VALUES ('delete', old.id, old.message_id, old.from_address, old.from_name, old.to_addresses, old.subject, old.body_text);
            END;
            """;
        await ExecuteNonQueryAsync(createDeleteTrigger, ct).ConfigureAwait(false);

        const string createUpdateTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, message_id, from_address, from_name, to_addresses, subject, body_text)
                VALUES ('delete', old.id, old.message_id, old.from_address, old.from_name, old.to_addresses, old.subject, old.body_text);
                INSERT INTO emails_fts(rowid, message_id, from_address, from_name, to_addresses, subject, body_text)
                VALUES (new.id, new.message_id, new.from_address, new.from_name, new.to_addresses, new.subject, new.body_text);
            END;
            """;
        await ExecuteNonQueryAsync(createUpdateTrigger, ct).ConfigureAwait(false);

        // Create B-tree indexes for structured queries
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);", ct).ConfigureAwait(false);

        // Create metadata table
        const string createMetadataTable = """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await ExecuteNonQueryAsync(createMetadataTable, ct).ConfigureAwait(false);

        _logger.LogInformation("Database initialized successfully");
    }

    /// <summary>
    /// Escapes special FTS5 characters in user input to prevent injection.
    /// </summary>
    public static string EscapeFts5Query(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // FTS5 special characters that need escaping: " * - + ( ) : ^
        // We wrap the entire input in quotes and escape internal quotes
        var escaped = input.Replace("\"", "\"\"");
        
        // For simple searches, wrap in quotes to treat as literal phrase
        // For advanced users who want to use FTS5 operators, they can use raw mode
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Prepares an FTS5 MATCH query with proper escaping.
    /// Supports wildcards if the input ends with *.
    /// </summary>
    public static string PrepareFts5MatchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Check if user wants wildcard search
        var trimmed = input.Trim();
        if (trimmed.EndsWith('*'))
        {
            // Remove the * and prepare for prefix search
            var prefix = trimmed[..^1].Replace("\"", "\"\"");
            return $"\"{prefix}\"*";
        }

        return EscapeFts5Query(trimmed);
    }

    /// <summary>
    /// Queries emails based on structured and/or full-text search criteria.
    /// </summary>
    public async Task<IReadOnlyList<EmailDocument>> QueryAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        // Build WHERE conditions for structured fields
        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("from_address LIKE @from");
                parameters["@from"] = query.FromAddress.Replace('*', '%');
            }
            else
            {
                conditions.Add("from_address = @from");
                parameters["@from"] = query.FromAddress;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            if (query.ToAddress.Contains('*'))
            {
                conditions.Add("to_addresses LIKE @to");
                parameters["@to"] = $"%{query.ToAddress.Replace('*', '%')}%";
            }
            else
            {
                conditions.Add("to_addresses LIKE @to");
                parameters["@to"] = $"%{query.ToAddress}%";
            }
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

        string sql;
        if (!string.IsNullOrWhiteSpace(query.ContentTerms))
        {
            // Full-text search with FTS5 - use properly escaped query
            var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);
            var whereClause = conditions.Count > 0 ? $"AND {string.Join(" AND ", conditions)}" : "";

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
            // Structured query only
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
            var orderBy = query.SortOrder switch
            {
                SearchSortOrder.DateAscending => "ORDER BY date_sent_unix ASC",
                _ => "ORDER BY date_sent_unix DESC"
            };

            sql = $"""
                SELECT * FROM emails
                {whereClause}
                {orderBy}
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
    /// Checks if an email with the given message ID already exists.
    /// </summary>
    public async Task<bool> EmailExistsAsync(string messageId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(1) FROM emails WHERE message_id = @messageId;";
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@messageId", messageId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result) > 0;
    }

    /// <summary>
    /// Inserts or updates a batch of emails.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(
        IReadOnlyList<EmailDocument> emails,
        CancellationToken ct = default)
    {
        if (emails.Count == 0) return;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            foreach (var email in emails)
            {
                await UpsertEmailInternalAsync(email, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task UpsertEmailInternalAsync(EmailDocument email, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name,
                to_addresses, cc_addresses, bcc_addresses,
                subject, date_sent_unix, date_received_unix,
                folder, account, has_attachments, attachment_names,
                body_preview, body_text, indexed_at_unix
            ) VALUES (
                @messageId, @filePath, @fromAddress, @fromName,
                @toAddresses, @ccAddresses, @bccAddresses,
                @subject, @dateSentUnix, @dateReceivedUnix,
                @folder, @account, @hasAttachments, @attachmentNames,
                @bodyPreview, @bodyText, @indexedAtUnix
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
                indexed_at_unix = excluded.indexed_at_unix;
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
        cmd.Parameters.AddWithValue("@dateSentUnix", (object?)email.DateSentUnix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dateReceivedUnix", (object?)email.DateReceivedUnix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", (object?)email.Folder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@account", (object?)email.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasAttachments", email.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@attachmentNames", (object?)email.AttachmentNamesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyPreview", (object?)email.BodyPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyText", (object?)email.BodyText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@indexedAtUnix", email.IndexedAtUnix);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds the database, dropping all data.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding database - all existing data will be deleted");

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        // Drop triggers first
        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_ai;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_ad;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_au;", ct).ConfigureAwait(false);

        // Drop tables
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS emails_fts;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS emails;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS index_metadata;", ct).ConfigureAwait(false);

        // Vacuum to reclaim space
        await ExecuteNonQueryAsync("VACUUM;", ct).ConfigureAwait(false);

        // Reinitialize
        await InitializeAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks database health by running integrity check.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureConnectionAsync(ct).ConfigureAwait(false);
            var result = await ExecuteScalarAsync<string>("PRAGMA integrity_check;", ct).ConfigureAwait(false);
            return result == "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return false;
        }
    }

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader)
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
            INSERT INTO index_metadata (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection != null && _connection.State == ConnectionState.Open)
            return;

        _connection?.Dispose();
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
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
