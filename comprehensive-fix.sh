#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Comprehensive Fix Script for MyImapDownloader
# Fixes all compilation errors identified in the code review
# =============================================================================

echo "==================================================================="
echo "Comprehensive Fix Script for MyImapDownloader & MyEmailSearch"
echo "==================================================================="

# -----------------------------------------------------------------------------
# FIX 1: Add missing methods to SearchDatabase.cs
# Methods: GetEmailCountAsync, IsHealthyAsync, GetKnownFilesAsync
# Also adds UpsertEmailAsync for tests
# -----------------------------------------------------------------------------
echo ""
echo "[FIX 1] Adding missing methods to SearchDatabase.cs"

# We need to add the missing methods to SearchDatabase.cs
# First, let's create a patch file approach - we'll append methods before DisposeAsync

cat > MyEmailSearch/Data/SearchDatabase.cs << 'EOF'
using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// SQLite database for email search with FTS5 full-text search.
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
        DatabasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        _logger = logger;
    }

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
            await EnsureConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets map of known file paths to their last modified ticks.
    /// Used for incremental indexing.
    /// </summary>
    public async Task<Dictionary<string, long>> GetKnownFilesAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT file_path, last_modified_ticks FROM emails;";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var path = reader.GetString(0);
            var ticks = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            result[path] = ticks;
        }
        return result;
    }

    /// <summary>
    /// Alias for GetKnownFilesAsync for compatibility.
    /// </summary>
    public async Task<Dictionary<string, long>> GetFilePathsWithModifiedTimesAsync(CancellationToken ct = default)
    {
        return await GetKnownFilesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Truncates all data from the index for rebuild.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        
        // Delete all data
        await ExecuteNonQueryAsync("DELETE FROM emails;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DELETE FROM emails_fts;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("DELETE FROM index_metadata;", ct).ConfigureAwait(false);
        
        // Vacuum to reclaim space
        await ExecuteNonQueryAsync("VACUUM;", ct).ConfigureAwait(false);
    }

    public long GetDatabaseSize()
    {
        if (!File.Exists(DatabasePath)) return 0;
        return new FileInfo(DatabasePath).Length;
    }

    /// <summary>
    /// Upserts a single email document.
    /// </summary>
    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        await UpsertEmailInternalAsync(email, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a single email document.
    /// </summary>
    public async Task InsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await UpsertEmailAsync(email, ct).ConfigureAwait(false);
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

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader)
    {
        long lastModified = 0;
        try { if (!reader.IsDBNull(reader.GetOrdinal("last_modified_ticks"))) lastModified = reader.GetInt64(reader.GetOrdinal("last_modified_ticks")); } catch { }

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
            HasAttachments = reader.GetInt64(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names")) ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview")) ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text")) ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix")),
            LastModifiedTicks = lastModified
        };
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

echo "   ✓ Updated SearchDatabase.cs with all required methods"

# -----------------------------------------------------------------------------
# FIX 2: Fix IndexManager.cs - Use correct method names
# -----------------------------------------------------------------------------
echo ""
echo "[FIX 2] Fixing IndexManager.cs to use GetKnownFilesAsync"

cat > MyEmailSearch/Indexing/IndexManager.cs << 'EOF'
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Manages the email search index lifecycle.
/// </summary>
public sealed class IndexManager
{
    private readonly SearchDatabase _database;
    private readonly ArchiveScanner _scanner;
    private readonly EmailParser _parser;
    private readonly ILogger<IndexManager> _logger;

    public IndexManager(
        SearchDatabase database,
        ArchiveScanner scanner,
        EmailParser parser,
        ILogger<IndexManager> logger)
    {
        _database = database;
        _scanner = scanner;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Performs incremental indexing - only indexes new or modified emails.
    /// </summary>
    public async Task<IndexingResult> IndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexingResult();

        _logger.LogInformation("Starting smart incremental index of {Path}", archivePath);

        // Load map of existing files and their timestamps
        var knownFiles = await _database.GetKnownFilesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Loaded {Count} existing file records from database", knownFiles.Count);

        var emailFiles = _scanner.ScanForEmails(archivePath).ToList();
        var batch = new List<EmailDocument>();
        var processed = 0;
        var total = emailFiles.Count;

        foreach (var file in emailFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(file);

                // Smart Scan Check:
                // If the file path exists in DB AND the last modified time matches exact ticks,
                // we skip it entirely. This prevents parsing.
                if (knownFiles.TryGetValue(file, out var existingTicks) &&
                    existingTicks == fileInfo.LastWriteTimeUtc.Ticks)
                {
                    result.Skipped++;
                    processed++;
                    progress?.Report(new IndexingProgress(processed, total, file));
                    continue;
                }

                // Parse the email
                var doc = await _parser.ParseAsync(file, includeContent, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    batch.Add(doc);
                    result.Indexed++;
                }

                // Batch insert
                if (batch.Count >= 100)
                {
                    await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index {File}", file);
                result.Errors++;
            }

            processed++;
            progress?.Report(new IndexingProgress(processed, total, file));
        }

        // Insert remaining batch
        if (batch.Count > 0)
        {
            await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
        }

        // Update metadata
        await _database.SetMetadataAsync("last_indexed_time", 
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ct).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Indexing complete: {Indexed} indexed, {Skipped} skipped, {Errors} errors in {Duration}",
            result.Indexed, result.Skipped, result.Errors, result.Duration);

        return result;
    }

    /// <summary>
    /// Rebuilds the entire index from scratch.
    /// </summary>
    public async Task<IndexingResult> RebuildIndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Starting full index rebuild - this will delete all existing data");

        // Clear existing data
        await _database.RebuildAsync(ct).ConfigureAwait(false);

        // Run full index
        return await IndexAsync(archivePath, includeContent, progress, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Result of an indexing operation.
/// </summary>
public sealed class IndexingResult
{
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Progress report for indexing operations.
/// </summary>
public sealed record IndexingProgress(int Processed, int Total, string? CurrentFile = null)
{
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}
EOF

echo "   ✓ Updated IndexManager.cs"

# -----------------------------------------------------------------------------
# FIX 3: Fix SmokeTests.cs - Use correct namespaces
# -----------------------------------------------------------------------------
echo ""
echo "[FIX 3] Fixing SmokeTests.cs namespace references"

cat > MyEmailSearch.Tests/SmokeTests.cs << 'EOF'
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify core types compile and are accessible.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task CoreTypes_AreAccessible()
    {
        // This test just verifies that the core types compile
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task QueryParser_CanBeInstantiated()
    {
        var parser = new QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task SnippetGenerator_CanBeInstantiated()
    {
        var generator = new SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }

    [Test]
    public async Task SearchQuery_HasDefaultValues()
    {
        var query = new SearchQuery();
        await Assert.That(query.Take).IsEqualTo(100);
        await Assert.That(query.Skip).IsEqualTo(0);
    }

    [Test]
    public async Task EmailDocument_CanBeCreated()
    {
        var doc = new EmailDocument
        {
            MessageId = "test@example.com",
            FilePath = "/test/path.eml",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await Assert.That(doc.MessageId).IsEqualTo("test@example.com");
    }
}
EOF

echo "   ✓ Updated SmokeTests.cs"

# -----------------------------------------------------------------------------
# FIX 4: Fix SearchDatabaseTests.cs - Remove 'with' expressions, use UpsertEmailAsync
# -----------------------------------------------------------------------------
echo ""
echo "[FIX 4] Fixing SearchDatabaseTests.cs"

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
        
        var email1 = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email1);
        
        // Create a new email with same file path but different subject
        var email2 = CreateTestEmail("test-1");
        email2.Subject = "Updated Subject";
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_InsertsMultipleEmails()
    {
        await _database.InitializeAsync();
        
        var email1 = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-2");
        await _database.UpsertEmailAsync(email2);

        var email3 = CreateTestEmail("test-3");
        await _database.UpsertEmailAsync(email3);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task BatchUpsert_InsertsMultipleEmails()
    {
        await _database.InitializeAsync();
        
        var emails = new List<EmailDocument>
        {
            CreateTestEmail("batch-1"),
            CreateTestEmail("batch-2"),
            CreateTestEmail("batch-3")
        };

        await _database.BatchUpsertEmailsAsync(emails);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsFilePaths()
    {
        await _database.InitializeAsync();
        
        var email = CreateTestEmail("known-file-test");
        email.LastModifiedTicks = 12345678;
        await _database.UpsertEmailAsync(email);

        var knownFiles = await _database.GetKnownFilesAsync();
        
        await Assert.That(knownFiles.ContainsKey(email.FilePath)).IsTrue();
        await Assert.That(knownFiles[email.FilePath]).IsEqualTo(12345678);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_WhenDatabaseIsValid()
    {
        await _database.InitializeAsync();
        
        var isHealthy = await _database.IsHealthyAsync();
        
        await Assert.That(isHealthy).IsTrue();
    }

    private static EmailDocument CreateTestEmail(string id)
    {
        return new EmailDocument
        {
            MessageId = $"{id}@test.com",
            FilePath = $"/test/emails/{id}.eml",
            FromAddress = "sender@test.com",
            Subject = $"Test Subject {id}",
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
        }
        catch { }
    }
}
EOF

echo "   ✓ Updated SearchDatabaseTests.cs"

# -----------------------------------------------------------------------------
# FIX 5: Ensure EmailDocument is a class with settable properties
# -----------------------------------------------------------------------------
echo ""
echo "[FIX 5] Ensuring EmailDocument has settable properties"

cat > MyEmailSearch/Data/EmailDocument.cs << 'EOF'
using System.Text.Json;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an indexed email document.
/// </summary>
public sealed class EmailDocument
{
    public long Id { get; set; }
    public required string MessageId { get; set; }
    public required string FilePath { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ToAddressesJson { get; set; }
    public string? CcAddressesJson { get; set; }
    public string? BccAddressesJson { get; set; }
    public string? Subject { get; set; }
    public long? DateSentUnix { get; set; }
    public long? DateReceivedUnix { get; set; }
    public string? Folder { get; set; }
    public string? Account { get; set; }
    public bool HasAttachments { get; set; }
    public string? AttachmentNamesJson { get; set; }
    public string? BodyPreview { get; set; }
    public string? BodyText { get; set; }
    public long IndexedAtUnix { get; set; }
    public long LastModifiedTicks { get; set; }

    // Convenience properties
    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string?> items)
    {
        var list = items.Where(i => i != null).ToList();
        return list.Count > 0 ? JsonSerializer.Serialize(list) : "";
    }
}
EOF

echo "   ✓ Updated EmailDocument.cs"

# -----------------------------------------------------------------------------
# Build and verify
# -----------------------------------------------------------------------------
echo ""
echo "==================================================================="
echo "Building and verifying fixes..."
echo "==================================================================="

echo ""
echo "Restoring packages..."
dotnet restore

echo ""
echo "Building solution..."
if dotnet build --no-restore -c Debug; then
    echo ""
    echo "✓ Build successful!"
    echo ""
    echo "Running tests..."
    if dotnet test --no-build -c Debug --verbosity normal; then
        echo ""
        echo "==================================================================="
        echo "✓ All fixes applied and verified successfully!"
        echo "==================================================================="
    else
        echo ""
        echo "⚠ Some tests failed. Please review the output above."
    fi
else
    echo ""
    echo "✗ Build failed. Please review the errors above."
    exit 1
fi

echo ""
echo "Summary of fixes applied:"
echo "  1. SearchDatabase.cs: Added GetEmailCountAsync, IsHealthyAsync, GetKnownFilesAsync, RebuildAsync, UpsertEmailAsync"
echo "  2. IndexManager.cs: Fixed to use GetKnownFilesAsync and RebuildAsync"
echo "  3. SmokeTests.cs: Fixed namespace references (MyEmailSearch.Search instead of MyEmailSearch.Tests.Search)"
echo "  4. SearchDatabaseTests.cs: Removed 'with' expressions, using direct property assignment"
echo "  5. EmailDocument.cs: Ensured it's a class with settable properties"
echo ""
echo "==================================================================="
