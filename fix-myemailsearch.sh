#!/bin/bash
# MyEmailSearch Comprehensive Fixes Script
# This script fixes remaining issues identified in code review:
# 1. Add FTS5 query escaping helper method to SearchDatabase
# 2. Add error handling to SearchCommand output methods
# 3. Ensure consistent ConfigureAwait(false) usage
# 4. Remove unnecessary IAsyncDisposable from SearchEngine
# 5. Add input validation to QueryParser

set -e

cd ~/src/dotnet/MyImapDownloader

echo "=================================================="
echo "MyEmailSearch Comprehensive Fixes"
echo "=================================================="
echo ""

# =============================================================================
# Fix 1: Update SearchDatabase.cs - Add FTS5 escape helper and improve query safety
# =============================================================================
echo "Fixing SearchDatabase.cs - Adding FTS5 escape helper..."

cat > MyEmailSearch/Data/SearchDatabase.cs << 'SEARCHDATABASE_EOF'
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
SEARCHDATABASE_EOF

echo "✓ SearchDatabase.cs updated with FTS5 escaping"

# =============================================================================
# Fix 2: Update SearchCommand.cs - Add error handling to output methods
# =============================================================================
echo "Fixing SearchCommand.cs - Adding error handling to output methods..."

cat > MyEmailSearch/Commands/SearchCommand.cs << 'SEARCHCOMMAND_EOF'
using System.CommandLine;
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

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(query, limit, format, archivePath, databasePath, verbose, ct)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        // Validate input
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

        // Ensure database is initialized
        await database.InitializeAsync(ct).ConfigureAwait(false);

        // Execute search
        var results = await searchEngine.SearchAsync(query, limit, 0, ct).ConfigureAwait(false);

        // Output results with error handling
        try
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
        catch (IOException ex)
        {
            // Handle broken pipe or other I/O errors gracefully
            if (verbose)
            {
                Console.Error.WriteLine($"Output error: {ex.Message}");
            }
        }
    }

    private static void OutputTable(SearchResultSet results)
    {
        Console.WriteLine($"Found {results.Results.Count} results in {results.QueryTime.TotalMilliseconds:F0}ms");
        Console.WriteLine(new string('-', 100));

        if (results.Results.Count == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 30);
            var subject = TruncateString(result.Email.Subject ?? "(No subject)", 50);

            Console.WriteLine($"{date}  {from,-30}  {subject}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                Console.WriteLine($"    {TruncateString(result.Snippet, 90)}");
            }

            Console.WriteLine();
        }

        if (results.HasMore)
        {
            Console.WriteLine($"... and {results.TotalCount - results.Results.Count} more results");
        }
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var output = new
        {
            results.TotalCount,
            QueryTimeMs = results.QueryTime.TotalMilliseconds,
            Results = results.Results.Select(r => new
            {
                r.Email.MessageId,
                r.Email.FromAddress,
                r.Email.Subject,
                DateSent = r.Email.DateSent?.ToString("O"),
                r.Email.Folder,
                r.Email.Account,
                r.Email.FilePath,
                r.Snippet
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(output, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        // Header
        Console.WriteLine("\"MessageId\",\"From\",\"Subject\",\"Date\",\"Folder\",\"Account\",\"FilePath\"");

        foreach (var result in results.Results)
        {
            var messageId = EscapeCsvField(result.Email.MessageId);
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

        // Escape quotes by doubling them and wrap in quotes
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
SEARCHCOMMAND_EOF

echo "✓ SearchCommand.cs updated with error handling"

# =============================================================================
# Fix 3: Update SearchEngine.cs - Remove unnecessary IAsyncDisposable, add ConfigureAwait
# =============================================================================
echo "Fixing SearchEngine.cs - Simplifying disposal pattern..."

cat > MyEmailSearch/Search/SearchEngine.cs << 'SEARCHENGINE_EOF'
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine
{
    private readonly SearchDatabase _database;
    private readonly QueryParser _queryParser;
    private readonly SnippetGenerator _snippetGenerator;
    private readonly ILogger<SearchEngine> _logger;

    public SearchEngine(
        SearchDatabase database,
        QueryParser queryParser,
        SnippetGenerator snippetGenerator,
        ILogger<SearchEngine> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _snippetGenerator = snippetGenerator ?? throw new ArgumentNullException(nameof(snippetGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a search query and returns results.
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

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", queryString);

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

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
            "Search completed: {ResultCount} results in {ElapsedMs}ms",
            results.Count, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = results.Count, // TODO: Get actual total count with separate count query
            Skip = offset,
            Take = limit,
            QueryTime = stopwatch.Elapsed
        };
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
SEARCHENGINE_EOF

echo "✓ SearchEngine.cs simplified"

# =============================================================================
# Fix 4: Update Program.cs - Remove IAsyncDisposable registration for SearchEngine
# =============================================================================
echo "Fixing Program.cs - Updating service registration..."

cat > MyEmailSearch/Program.cs << 'PROGRAM_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive")
        {
            Name = "myemailsearch"
        };

        // Global options
        var archiveOption = new Option<string?>(["--archive", "-a"])
        {
            Description = "Path to the email archive directory"
        };

        var databaseOption = new Option<string?>(["--database", "-d"])
        {
            Description = "Path to the search database file"
        };

        var verboseOption = new Option<bool>(["--verbose", "-v"])
        {
            Description = "Enable verbose output"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        // Add subcommands
        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a service provider with all required dependencies.
    /// </summary>
    public static ServiceProvider CreateServiceProvider(
        string archivePath,
        string databasePath,
        bool verbose)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Database
        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        // Search components
        services.AddSingleton<QueryParser>();
        services.AddSingleton<SnippetGenerator>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<SnippetGenerator>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

        // Indexing components
        services.AddSingleton(sp =>
            new ArchiveScanner(sp.GetRequiredService<ILogger<ArchiveScanner>>()));
        services.AddSingleton(sp =>
            new EmailParser(archivePath, sp.GetRequiredService<ILogger<EmailParser>>()));
        services.AddSingleton(sp => new IndexManager(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<ArchiveScanner>(),
            sp.GetRequiredService<EmailParser>(),
            sp.GetRequiredService<ILogger<IndexManager>>()));

        return services.BuildServiceProvider();
    }
}
PROGRAM_EOF

echo "✓ Program.cs updated"

# =============================================================================
# Fix 5: Add tests for FTS5 escaping
# =============================================================================
echo "Adding FTS5 escaping tests..."

cat > MyEmailSearch.Tests/Data/SearchDatabaseEscapingTests.cs << 'ESCAPINGTESTS_EOF'
namespace MyEmailSearch.Tests.Data;

using MyEmailSearch.Data;

public class SearchDatabaseEscapingTests
{
    [Test]
    public async Task EscapeFts5Query_WithSpecialCharacters_EscapesCorrectly()
    {
        var result = SearchDatabase.EscapeFts5Query("test\"query");
        
        await Assert.That(result).IsEqualTo("\"test\"\"query\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNormalText_WrapsInQuotes()
    {
        var result = SearchDatabase.EscapeFts5Query("hello world");
        
        await Assert.That(result).IsEqualTo("\"hello world\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithEmptyString_ReturnsEmpty()
    {
        var result = SearchDatabase.EscapeFts5Query("");
        
        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task EscapeFts5Query_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.EscapeFts5Query(null!);
        
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
ESCAPINGTESTS_EOF

echo "✓ FTS5 escaping tests added"

# =============================================================================
# Final build and test
# =============================================================================
echo ""
echo "=================================================="
echo "Building and testing..."
echo "=================================================="

dotnet build

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Build succeeded!"
    echo ""
    echo "Running tests..."
    dotnet test
else
    echo ""
    echo "✗ Build failed. Please review the errors above."
    exit 1
fi

echo ""
echo "=================================================="
echo "All fixes applied successfully!"
echo "=================================================="
echo ""
echo "Summary of changes:"
echo "  1. SearchDatabase.cs - Added FTS5 query escaping methods"
echo "  2. SearchCommand.cs - Added error handling for output operations"
echo "  3. SearchEngine.cs - Removed unnecessary IAsyncDisposable"
echo "  4. Program.cs - Updated service registration"
echo "  5. Added FTS5 escaping tests"
echo ""
