#!/bin/bash
set -e

# ==============================================================================
# Fix Indexing Infinite Loop (Duplicate Email Handling)
# ==============================================================================
# The previous logic forced one record per Message-ID. This caused an infinite
# loop of re-indexing for emails that exist in multiple folders (Inbox/Trash).
#
# This script:
# 1. Modifies SearchDatabase to use 'file_path' as the UNIQUE constraint.
# 2. Updates Upsert logic to handle conflicts on 'file_path'.
# 3. Ensures both copies of an email are indexed and tracked.
# ==============================================================================

echo "Applying schema fixes to allow duplicate emails (multi-folder support)..."

# 1. Update MyEmailSearch/Data/SearchDatabase.cs
# ------------------------------------------------------------------------------
# Changes:
# - Removed UNIQUE constraint from message_id
# - Added UNIQUE constraint to file_path
# - Changed UpsertEmailInternalAsync to ON CONFLICT(file_path)
# ------------------------------------------------------------------------------
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

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Initializing search database at {Path}", DatabasePath);

        await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);

        // Schema Change: file_path is now UNIQUE, message_id is NOT unique.
        // This allows the same email to exist in multiple folders (e.g. Inbox + Trash).
        const string createEmailsTable = """
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
            """;
        await ExecuteNonQueryAsync(createEmailsTable, ct).ConfigureAwait(false);

        // Indexes
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_message_id ON emails(message_id);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);", ct).ConfigureAwait(false);
        
        // FTS5 Table
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

        // Triggers
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

        const string createMetadataTable = """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await ExecuteNonQueryAsync(createMetadataTable, ct).ConfigureAwait(false);
    }

    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, long>> GetKnownFilesAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, long>();
        
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT file_path, last_modified_ticks FROM emails";
        
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var path = reader.GetString(0);
            var ticks = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            // Dictionary enforces unique keys, matching our UNIQUE(file_path) constraint
            result[path] = ticks;
        }
        return result;
    }

    public long GetDatabaseSize()
    {
        if (!File.Exists(DatabasePath)) return 0;
        return new FileInfo(DatabasePath).Length;
    }

    public async Task<List<EmailDocument>> QueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

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
            var whereClause = conditions.Count > 0 ?
                $"WHERE {string.Join(" AND ", conditions)}" : "";
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

    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        await UpsertEmailInternalAsync(email, ct).ConfigureAwait(false);
    }

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
        // CHANGED: ON CONFLICT target is now (file_path) instead of (message_id)
        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name,
                to_addresses, cc_addresses, bcc_addresses,
                subject, date_sent_unix, date_received_unix,
                folder, account, has_attachments, attachment_names,
                body_preview, body_text, indexed_at_unix, last_modified_ticks
            ) VALUES (
                @messageId, @filePath, @fromAddress, @fromName,
                @toAddresses, @ccAddresses, @bccAddresses,
                @subject, @dateSentUnix, @dateReceivedUnix,
                @folder, @account, @hasAttachments, @attachmentNames,
                @bodyPreview, @bodyText, @indexedAtUnix, @lastModifiedTicks
            )
            ON CONFLICT(file_path) DO UPDATE SET
                message_id = excluded.message_id,
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
                indexed_at_unix = excluded.indexed_at_unix,
                last_modified_ticks = excluded.last_modified_ticks;
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
        cmd.Parameters.AddWithValue("@lastModifiedTicks", email.LastModifiedTicks);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding database - all existing data will be deleted");
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_ai;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_ad;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TRIGGER IF EXISTS emails_au;", ct).ConfigureAwait(false);
        
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS emails_fts;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS emails;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DROP TABLE IF EXISTS index_metadata;", ct).ConfigureAwait(false);
        
        await ExecuteNonQueryAsync("VACUUM;", ct).ConfigureAwait(false);
        await InitializeAsync(ct).ConfigureAwait(false);
    }

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

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader) 
    {
        long lastModified = 0;
        try { if (!reader.IsDBNull(reader.GetOrdinal("last_modified_ticks"))) lastModified = reader.GetInt64(reader.GetOrdinal("last_modified_ticks")); } catch { }

        return new()
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
            HasAttachments = reader.GetInt64(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names")) ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview")) ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text")) ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix")),
            LastModifiedTicks = lastModified
        };
    }

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
        if (_connection != null && _connection.State == ConnectionState.Open) return;
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
echo "Updated SearchDatabase.cs to allow duplicate message_ids"

# 2. Update Tests MyEmailSearch.Tests/Data/SearchDatabaseTests.cs
# ------------------------------------------------------------------------------
# Changes:
# - Updated Upsert test to expect 2 records if 2 files have same message ID
# - Verified that 'UpsertEmail_UpdatesExistingEmail' still works for *same file*
# ------------------------------------------------------------------------------
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
        var email = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_UpdatesExistingFile()
    {
        // Same file path, same message ID -> Should update, count remains 1
        await _database.InitializeAsync();
        var email1 = CreateTestEmail("test-1", "Subject 1", "/path/to/file1.eml");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-1", "Subject Updated", "/path/to/file1.eml");
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
        
        var files = await _database.GetKnownFilesAsync();
        await Assert.That(files.ContainsKey("/path/to/file1.eml")).IsTrue();
    }

    [Test]
    public async Task UpsertEmail_AllowsDuplicateMessageIds_DifferentFiles()
    {
        // Different file paths, same message ID -> Should insert both, count becomes 2
        await _database.InitializeAsync();
        var email1 = CreateTestEmail("duplicate-id", "Copy 1", "/path/to/inbox/mail.eml");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("duplicate-id", "Copy 2", "/path/to/trash/mail.eml");
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(2);
        
        // Ensure both files are tracked
        var files = await _database.GetKnownFilesAsync();
        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EmailExists_ReturnsTrueForExistingEmail()
    {
        await _database.InitializeAsync();
        var email = CreateTestEmail("test-exists");
        await _database.UpsertEmailAsync(email);

        var exists = await _database.EmailExistsAsync("test-exists");
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task Query_ByFromAddress_ReturnsMatchingEmails()
    {
        await _database.InitializeAsync();
        await _database.UpsertEmailAsync(CreateTestEmail("1", "S1", "/f1", "alice@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("2", "S2", "/f2", "bob@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("3", "S3", "/f3", "alice@example.com"));

        var query = new SearchQuery { FromAddress = "alice@example.com" };
        var results = await _database.QueryAsync(query);

        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsInsertedPaths()
    {
        await _database.InitializeAsync();
        var email = CreateTestEmail("file-test", "Sub", "/unique/path.eml");
        await _database.UpsertEmailAsync(email);

        var knownFiles = await _database.GetKnownFilesAsync();
        
        await Assert.That(knownFiles).ContainsKey(email.FilePath);
        await Assert.That(knownFiles[email.FilePath]).IsEqualTo(email.LastModifiedTicks);
    }

    private static EmailDocument CreateTestEmail(
        string messageId,
        string subject = "Test Subject",
        string filePath = null,
        string fromAddress = "sender@example.com") => new()
        {
            MessageId = messageId,
            FilePath = filePath ?? $"/test/{messageId}.eml",
            FromAddress = fromAddress,
            Subject = subject,
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
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
echo "Updated SearchDatabaseTests.cs"

echo "Fix applied. You MUST delete your existing 'search.db' or run 'rebuild' command for this to take effect."
