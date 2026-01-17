#!/bin/bash
set -euo pipefail

# Complete fix for MyEmailSearch compilation errors
# Based on actual dump.txt content

PROJECT_ROOT="${PROJECT_ROOT:-$HOME/src/dotnet/MyImapDownloader}"

echo "=========================================="
echo "MyEmailSearch: Complete Fix"
echo "=========================================="
echo ""

cd "$PROJECT_ROOT"

# ==============================================================================
# 1. Fix SearchCommand.cs - Fix Option constructor syntax + add --open flag
# ==============================================================================

echo "1. Fixing SearchCommand.cs..."

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

        var limitOption = new Option<int>("--limit", "Maximum number of results to return")
        {
            DefaultValueFactory = _ => 100
        };
        limitOption.AddAlias("-l");

        var formatOption = new Option<string>("--format", "Output format: table, json, or csv")
        {
            DefaultValueFactory = _ => "table"
        };
        formatOption.AddAlias("-f");

        var openOption = new Option<bool>("--open", "Interactively select and open an email in your default application")
        {
            DefaultValueFactory = _ => false
        };
        openOption.AddAlias("-o");

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
        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();

        var displayCount = Math.Min(results.Results.Count, 20);
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
            process?.WaitForExit(1000);
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

echo "   ✓ SearchCommand.cs fixed"

# ==============================================================================
# 2. Fix IndexManager.cs - Use correct method names
# ==============================================================================

echo "2. Fixing IndexManager.cs..."

cat > "$PROJECT_ROOT/MyEmailSearch/Indexing/IndexManager.cs" << 'ENDOFFILE'
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Manages the email search index lifecycle.
/// </summary>
public sealed class IndexManager(
    SearchDatabase database,
    ArchiveScanner scanner,
    EmailParser parser,
    ILogger<IndexManager> logger)
{
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

        logger.LogInformation("Starting smart incremental index of {Path}", archivePath);

        // Load map of existing files and their timestamps
        var knownFiles = await database.GetKnownFilesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Loaded {Count} existing file records from database", knownFiles.Count);

        var emailFiles = scanner.ScanForEmails(archivePath).ToList();
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
                var doc = await parser.ParseAsync(file, includeContent, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    batch.Add(doc);
                    result.Indexed++;
                }

                // Batch insert
                if (batch.Count >= 100)
                {
                    await database.UpsertEmailsAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to index {File}", file);
                result.Errors++;
            }

            processed++;
            progress?.Report(new IndexingProgress(processed, total, file));
        }

        // Insert remaining batch
        if (batch.Count > 0)
        {
            await database.UpsertEmailsAsync(batch, ct).ConfigureAwait(false);
        }

        // Update metadata
        await database.SetMetadataAsync("last_indexed_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ct).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        logger.LogInformation(
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
        logger.LogWarning("Starting full index rebuild - this will delete all existing data");

        // Clear existing data and reinitialize
        await database.ClearAllDataAsync(ct).ConfigureAwait(false);
        await database.InitializeAsync(ct).ConfigureAwait(false);

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
ENDOFFILE

echo "   ✓ IndexManager.cs fixed"

# ==============================================================================
# 3. Fix SearchDatabase.cs - Add GetTotalCountForQueryAsync
# ==============================================================================

echo "3. Fixing SearchDatabase.cs (adding GetTotalCountForQueryAsync)..."

cat > "$PROJECT_ROOT/MyEmailSearch/Data/SearchDatabase.cs" << 'ENDOFFILE'
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// SQLite database for email search with FTS5 full-text search.
/// </summary>
public sealed partial class SearchDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SearchDatabase> _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public string DatabasePath { get; }

    public SearchDatabase(string databasePath, ILogger<SearchDatabase> logger)
    {
        DatabasePath = databasePath;
        _logger = logger;
        _connectionString = $"Data Source={databasePath}";
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
ENDOFFILE

echo "   ✓ SearchDatabase.cs fixed"

# ==============================================================================
# 4. Fix SearchEngine.cs - Use GetTotalCountForQueryAsync
# ==============================================================================

echo "4. Fixing SearchEngine.cs..."

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

        // FIX: Get actual total count (without LIMIT) for accurate pagination
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

echo "   ✓ SearchEngine.cs fixed"

# ==============================================================================
# 5. Build and verify
# ==============================================================================

echo ""
echo "5. Building..."

cd "$PROJECT_ROOT"

if dotnet build MyEmailSearch/MyEmailSearch.csproj -c Release --nologo; then
    echo ""
    echo "=========================================="
    echo "✓ Build SUCCESSFUL!"
    echo "=========================================="
else
    echo ""
    echo "=========================================="
    echo "✗ Build failed - check errors above"
    echo "=========================================="
    exit 1
fi

echo ""
echo "Running tests..."
if dotnet test MyEmailSearch.Tests/MyEmailSearch.Tests.csproj --nologo -v q 2>/dev/null; then
    echo "   ✓ Tests passed"
else
    echo "   ⚠ Some tests may have failed"
fi

echo ""
echo "Changes made:"
echo "  1. SearchCommand.cs - Fixed Option constructor syntax (use string + AddAlias)"
echo "     Added --open flag for interactive email selection"
echo ""
echo "  2. IndexManager.cs - Fixed method names:"
echo "     - BatchUpsertEmailsAsync → UpsertEmailsAsync"
echo "     - RebuildAsync → ClearAllDataAsync + InitializeAsync"
echo ""
echo "  3. SearchDatabase.cs - Added GetTotalCountForQueryAsync()"
echo "     Fixes the TotalCount bug where limit was reported as total"
echo ""
echo "  4. SearchEngine.cs - Now uses GetTotalCountForQueryAsync()"
echo "     TotalCount now reflects actual matching records, not limited count"
echo ""
echo "Usage:"
echo "  myemailsearch search 'to:level3@tilde.team'        # Shows correct total"
echo "  myemailsearch search 'to:level3@tilde.team' --open # Interactive selection"
echo ""
ENDOFFILE
