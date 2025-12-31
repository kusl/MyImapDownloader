#!/bin/bash
# Comprehensive fix for MyEmailSearch.Tests build errors
# Run from repository root: ~/src/dotnet/MyImapDownloader

set -e

echo "=== Fixing MyEmailSearch.Tests build errors ==="

# Fix 1: Update SmokeTests.cs to use correct namespaces
echo "Fixing SmokeTests.cs..."
cat > MyEmailSearch.Tests/SmokeTests.cs << 'EOF'
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify the project builds and runs.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task Project_Builds_Successfully()
    {
        // This test passes if the project compiles
        var result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Can_Create_QueryParser()
    {
        var parser = new QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task Can_Create_SnippetGenerator()
    {
        var generator = new SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }
}
EOF

# Fix 2: Update EmailDocument.cs to be a record instead of a class
# This enables 'with' expressions used in tests
echo "Fixing EmailDocument.cs to be a record..."
cat > MyEmailSearch/Data/EmailDocument.cs << 'EOF'
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an email document stored in the search index.
/// </summary>
public sealed record EmailDocument
{
    public long Id { get; init; }
    public required string MessageId { get; init; }
    public required string FilePath { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? ToAddressesJson { get; init; }
    public string? CcAddressesJson { get; init; }
    public string? BccAddressesJson { get; init; }
    public string? Subject { get; init; }
    public long? DateSentUnix { get; init; }
    public long? DateReceivedUnix { get; init; }
    public string? Folder { get; init; }
    public string? Account { get; init; }
    public bool HasAttachments { get; init; }
    public string? AttachmentNamesJson { get; init; }
    public string? BodyPreview { get; init; }
    public string? BodyText { get; init; }
    public long IndexedAtUnix { get; init; }

    // Computed properties
    [JsonIgnore]
    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    [JsonIgnore]
    public DateTimeOffset? DateReceived => DateReceivedUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateReceivedUnix.Value)
        : null;

    [JsonIgnore]
    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string>? items)
    {
        if (items == null) return "[]";
        return JsonSerializer.Serialize(items.ToList());
    }
}
EOF

# Fix 3: Rewrite SearchDatabase.cs with UpsertEmailAsync method
echo "Rewriting SearchDatabase.cs with UpsertEmailAsync method..."
cat > MyEmailSearch/Data/SearchDatabase.cs << 'EOF'
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
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Initializing search database at {Path}", DatabasePath);

        // Enable WAL mode for better concurrent access
        await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);

        // Create main emails table
        const string createEmailsTable = """
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id TEXT NOT NULL UNIQUE,
                file_path TEXT NOT NULL,
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
                indexed_at_unix INTEGER NOT NULL
            );
            """;
        await ExecuteNonQueryAsync(createEmailsTable, ct).ConfigureAwait(false);

        // Create indexes for common queries
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);", ct).ConfigureAwait(false);

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

        // Create triggers to keep FTS index in sync
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

        // Create metadata table for tracking index state
        const string createMetadataTable = """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await ExecuteNonQueryAsync(createMetadataTable, ct).ConfigureAwait(false);

        _logger.LogInformation("Search database initialized successfully");
    }

    /// <summary>
    /// Gets the total number of indexed emails.
    /// </summary>
    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the database file size in bytes.
    /// </summary>
    public long GetDatabaseSize()
    {
        if (!File.Exists(DatabasePath)) return 0;
        return new FileInfo(DatabasePath).Length;
    }

    /// <summary>
    /// Queries emails based on search criteria.
    /// </summary>
    public async Task<List<EmailDocument>> QueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        // Build WHERE conditions
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

        string sql;
        var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            // Full-text search with optional structured conditions
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
    /// Inserts or updates a single email.
    /// </summary>
    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        await UpsertEmailInternalAsync(email, ct).ConfigureAwait(false);
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

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result?.ToString() == "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return false;
        }
    }

    /// <summary>
    /// Prepares a search string for FTS5 MATCH query.
    /// Escapes special characters and handles wildcards.
    /// </summary>
    public static string? PrepareFts5MatchQuery(string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(searchTerms))
            return null;

        var trimmed = searchTerms.Trim();
        
        // Check if ends with wildcard
        var hasWildcard = trimmed.EndsWith('*');
        if (hasWildcard)
        {
            trimmed = trimmed[..^1]; // Remove the trailing *
        }

        // Wrap in quotes to escape FTS5 operators
        var escaped = $"\"{trimmed}\"";

        // Re-add wildcard outside quotes if needed
        if (hasWildcard)
        {
            escaped += "*";
        }

        return escaped;
    }

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader) => new()
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
        HasAttachments = reader.GetInt64(reader.GetOrdinal("has_attachments")) == 1,
        AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names"))
            ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
        BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview"))
            ? null : reader.GetString(reader.GetOrdinal("body_preview")),
        BodyText = reader.IsDBNull(reader.GetOrdinal("body_text"))
            ? null : reader.GetString(reader.GetOrdinal("body_text")),
        IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix"))
    };

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
EOF

# Fix 4: Update SearchDatabaseTests.cs to use proper patterns
echo "Fixing SearchDatabaseTests.cs..."
cat > MyEmailSearch.Tests/Data/SearchDatabaseTests.cs << 'EOF'
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

public class SearchDatabaseTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SearchDatabase _database;

    public SearchDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _database = new SearchDatabase(_dbPath, NullLogger<SearchDatabase>.Instance);
    }

    [Test]
    public async Task Initialize_CreatesDatabase()
    {
        await _database.InitializeAsync();

        await Assert.That(File.Exists(_dbPath)).IsTrue();
    }

    [Test]
    public async Task UpsertEmail_InsertsNewEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-1@example.com");
        await _database.UpsertEmailAsync(email);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_UpdatesExistingEmail()
    {
        await _database.InitializeAsync();

        var email1 = CreateTestEmail("test-1@example.com", "Original");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-1@example.com", "Updated");
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task EmailExists_ReturnsTrueForExistingEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-exists@example.com");
        await _database.UpsertEmailAsync(email);

        var exists = await _database.EmailExistsAsync("test-exists@example.com");
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task EmailExists_ReturnsFalseForNonExistingEmail()
    {
        await _database.InitializeAsync();

        var exists = await _database.EmailExistsAsync("nonexistent@example.com");
        await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task Query_ByFromAddress_ReturnsMatchingEmails()
    {
        await _database.InitializeAsync();

        await _database.UpsertEmailAsync(CreateTestEmail("test-1", fromAddress: "alice@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("test-2", fromAddress: "bob@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("test-3", fromAddress: "alice@example.com"));

        var query = new SearchQuery { FromAddress = "alice@example.com" };
        var results = await _database.QueryAsync(query);

        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IsHealthy_ReturnsTrueForHealthyDatabase()
    {
        await _database.InitializeAsync();

        var healthy = await _database.IsHealthyAsync();

        await Assert.That(healthy).IsTrue();
    }

    private static EmailDocument CreateTestEmail(
        string messageId, 
        string? subject = "Test Subject",
        string? fromAddress = "sender@example.com") => new()
    {
        MessageId = messageId,
        FilePath = $"/test/{messageId}.eml",
        FromAddress = fromAddress,
        Subject = subject,
        DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();

        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch { /* Ignore cleanup errors */ }
    }
}
EOF

# Fix 5: Ensure Fts5HelperTests.cs is correct
echo "Fixing Fts5HelperTests.cs..."
cat > MyEmailSearch.Tests/Data/Fts5HelperTests.cs << 'EOF'
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

public class Fts5HelperTests
{
    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmptyString_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWhitespace_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("   ");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test*");

        await Assert.That(result).IsEqualTo("\"test\"*");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithoutWildcard_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test query");

        await Assert.That(result).IsEqualTo("\"test query\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithFts5Operators_EscapesThem()
    {
        // Users shouldn't be able to inject FTS5 operators like OR, AND, NOT
        var result = SearchDatabase.PrepareFts5MatchQuery("test OR hack");

        await Assert.That(result).IsEqualTo("\"test OR hack\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithParentheses_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("(test)");

        await Assert.That(result).IsEqualTo("\"(test)\"");
    }
}
EOF

echo ""
echo "=== Building to verify fixes ==="
dotnet build

echo ""
echo "=== All fixes applied successfully! ==="
