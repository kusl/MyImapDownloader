#!/bin/bash
# Fix MyEmailSearch command files for System.CommandLine 2.0.0-beta5+ API
# Run from the MyImapDownloader root directory

set -e

SEARCH_DIR="MyEmailSearch"

echo "Fixing MyEmailSearch command files..."

# Backup originals
mkdir -p "$SEARCH_DIR/.backup"
cp "$SEARCH_DIR/Program.cs" "$SEARCH_DIR/.backup/" 2>/dev/null || true
cp "$SEARCH_DIR/Commands/"*.cs "$SEARCH_DIR/.backup/" 2>/dev/null || true

# =============================================================================
# Program.cs
# =============================================================================
cat > "$SEARCH_DIR/Program.cs" << 'PROGRAM_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

/// <summary>
/// Entry point for the MyEmailSearch CLI application.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MyEmailSearch - Full-text search for email archives");

        // Global options - use string array for aliases
        var archiveOption = new Option<string?>(new[] { "--archive", "-a" })
        {
            Description = "Path to the email archive directory"
        };

        var databaseOption = new Option<string?>(new[] { "--database", "-d" })
        {
            Description = "Path to the search index database"
        };

        var verboseOption = new Option<bool>(new[] { "--verbose", "-v" })
        {
            Description = "Enable verbose output"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        // Add subcommands - pass global options so they can access them
        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Creates the DI service provider with all required services.
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

echo "  ✓ Program.cs"

# =============================================================================
# Commands/SearchCommand.cs
# =============================================================================
cat > "$SEARCH_DIR/Commands/SearchCommand.cs" << 'SEARCH_EOF'
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
            Description = "Search query (e.g., 'from:alice@example.com kafka')"
        };

        var limitOption = new Option<int>(new[] { "--limit", "-l" })
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>(new[] { "--format", "-f" })
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

            await ExecuteAsync(query, limit, format, archivePath, databasePath, verbose, ct);
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
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Error: Search index not found at '{databasePath}'");
            Console.Error.WriteLine("Run 'myemailsearch index' first to build the search index.");
            return;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        var results = await searchEngine.SearchAsync(query, limit, 0, ct);

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
            var from = Truncate(result.Email.FromAddress ?? "Unknown", 28);
            var subject = Truncate(result.Email.Subject ?? "(no subject)", 48);

            Console.WriteLine($"{date,-12} {from,-30} {subject,-50}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                Console.WriteLine($"             {result.Snippet}");
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
        Console.WriteLine("Date,From,To,Subject,FilePath");
        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "";
            var from = EscapeCsv(result.Email.FromAddress ?? "");
            var to = EscapeCsv(string.Join("; ", result.Email.ToAddresses));
            var subject = EscapeCsv(result.Email.Subject ?? "");
            var path = EscapeCsv(result.Email.FilePath);
            Console.WriteLine($"{date},{from},{to},{subject},{path}");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
SEARCH_EOF

echo "  ✓ Commands/SearchCommand.cs"

# =============================================================================
# Commands/IndexCommand.cs
# =============================================================================
cat > "$SEARCH_DIR/Commands/IndexCommand.cs" << 'INDEX_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'index' command for building/updating the search index.
/// </summary>
public static class IndexCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var fullOption = new Option<bool>(new[] { "--full", "-f" })
        {
            Description = "Force full re-index (ignore incremental state)"
        };

        var contentOption = new Option<bool>("--content")
        {
            Description = "Index email body content for full-text search"
        };

        var command = new Command("index", "Build or update the search index");
        command.Options.Add(fullOption);
        command.Options.Add(contentOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var full = parseResult.GetValue(fullOption);
            var content = parseResult.GetValue(contentOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(full, content, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool full,
        bool content,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        Console.WriteLine($"Indexing emails from: {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine($"Mode: {(full ? "Full rebuild" : "Incremental")}");
        Console.WriteLine($"Index content: {content}");
        Console.WriteLine();

        if (!Directory.Exists(archivePath))
        {
            Console.Error.WriteLine($"Error: Archive directory not found: {archivePath}");
            return;
        }

        // Ensure database directory exists
        var dbDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var indexManager = sp.GetRequiredService<IndexManager>();

        // Initialize database
        await database.InitializeAsync(ct);

        var progress = new Progress<IndexingProgress>(p =>
        {
            var pct = p.Total > 0 ? (double)p.Processed / p.Total * 100 : 0;
            Console.Write($"\rProcessing: {p.Processed:N0}/{p.Total:N0} ({pct:F1}%) - {p.CurrentFile ?? ""}".PadRight(100)[..100]);
        });

        IndexingResult result;
        if (full)
        {
            result = await indexManager.RebuildIndexAsync(archivePath, content, progress, ct);
        }
        else
        {
            result = await indexManager.IndexAsync(archivePath, content, progress, ct);
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Indexing complete:");
        Console.WriteLine($"  New emails indexed: {result.Indexed:N0}");
        Console.WriteLine($"  Skipped (existing): {result.Skipped:N0}");
        Console.WriteLine($"  Errors:             {result.Errors:N0}");
        Console.WriteLine($"  Duration:           {result.Duration}");
    }
}
INDEX_EOF

echo "  ✓ Commands/IndexCommand.cs"

# =============================================================================
# Commands/RebuildCommand.cs
# =============================================================================
cat > "$SEARCH_DIR/Commands/RebuildCommand.cs" << 'REBUILD_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'rebuild' command for completely rebuilding the search index.
/// </summary>
public static class RebuildCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var confirmOption = new Option<bool>(new[] { "--yes", "-y" })
        {
            Description = "Skip confirmation prompt"
        };

        var contentOption = new Option<bool>("--content")
        {
            Description = "Also index email body content"
        };

        var command = new Command("rebuild", "Rebuild the entire search index from scratch");
        command.Options.Add(confirmOption);
        command.Options.Add(contentOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            var content = parseResult.GetValue(contentOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(confirm, content, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool confirm,
        bool content,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        if (!confirm)
        {
            Console.Write("This will delete and rebuild the entire index. Continue? [y/N]: ");
            var response = Console.ReadLine();
            if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        Console.WriteLine("Rebuilding index...");
        Console.WriteLine($"Archive path: {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine();

        // Ensure database directory exists
        var dbDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var indexManager = sp.GetRequiredService<IndexManager>();

        var progress = new Progress<IndexingProgress>(p =>
        {
            var pct = p.Total > 0 ? (double)p.Processed / p.Total * 100 : 0;
            Console.Write($"\rProcessing: {p.Processed:N0}/{p.Total:N0} ({pct:F1}%)".PadRight(60));
        });

        var result = await indexManager.RebuildIndexAsync(archivePath, content, progress, ct);

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Rebuild complete:");
        Console.WriteLine($"  Indexed: {result.Indexed:N0}");
        Console.WriteLine($"  Errors:  {result.Errors:N0}");
        Console.WriteLine($"  Time:    {result.Duration}");
    }
}
REBUILD_EOF

echo "  ✓ Commands/RebuildCommand.cs"

# =============================================================================
# Commands/StatusCommand.cs
# =============================================================================
cat > "$SEARCH_DIR/Commands/StatusCommand.cs" << 'STATUS_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'status' command for displaying index statistics.
/// </summary>
public static class StatusCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var command = new Command("status", "Show search index statistics");

        command.SetAction(async (parseResult, ct) =>
        {
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        Console.WriteLine("MyEmailSearch Index Status");
        Console.WriteLine("==========================");
        Console.WriteLine();

        Console.WriteLine($"Archive path:  {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine();

        if (!File.Exists(databasePath))
        {
            Console.WriteLine("Status: No index found");
            Console.WriteLine();
            Console.WriteLine("Run 'myemailsearch index' to build the search index.");
            return;
        }

        var fileInfo = new FileInfo(databasePath);
        Console.WriteLine($"Database size: {FormatBytes(fileInfo.Length)}");
        Console.WriteLine($"Last modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();

        await database.InitializeAsync(ct);
        var stats = await database.GetStatisticsAsync(ct);

        Console.WriteLine("Index Statistics:");
        Console.WriteLine($"  Total emails:      {stats.TotalEmails:N0}");
        Console.WriteLine($"  Unique senders:    {stats.UniqueSenders:N0}");
        Console.WriteLine($"  Date range:        {stats.OldestEmail:yyyy-MM-dd} to {stats.NewestEmail:yyyy-MM-dd}");
        Console.WriteLine($"  With attachments:  {stats.EmailsWithAttachments:N0}");
        Console.WriteLine();

        if (stats.AccountCounts.Count > 0)
        {
            Console.WriteLine("Emails by Account:");
            foreach (var (account, count) in stats.AccountCounts.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {account,-30} {count,10:N0}");
            }
            Console.WriteLine();
        }

        if (stats.FolderCounts.Count > 0 && verbose)
        {
            Console.WriteLine("Emails by Folder:");
            foreach (var (folder, count) in stats.FolderCounts.OrderByDescending(x => x.Value).Take(20))
            {
                Console.WriteLine($"  {folder,-40} {count,10:N0}");
            }
            if (stats.FolderCounts.Count > 20)
            {
                Console.WriteLine($"  ... and {stats.FolderCounts.Count - 20} more folders");
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int order = 0;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
STATUS_EOF

echo "  ✓ Commands/StatusCommand.cs"

# =============================================================================
# Data/IndexStatistics.cs (if missing)
# =============================================================================
if [ ! -f "$SEARCH_DIR/Data/IndexStatistics.cs" ]; then
cat > "$SEARCH_DIR/Data/IndexStatistics.cs" << 'STATS_EOF'
namespace MyEmailSearch.Data;

/// <summary>
/// Statistics about the search index.
/// </summary>
public sealed record IndexStatistics
{
    public long TotalEmails { get; init; }
    public long UniqueSenders { get; init; }
    public DateTimeOffset OldestEmail { get; init; }
    public DateTimeOffset NewestEmail { get; init; }
    public long EmailsWithAttachments { get; init; }
    public Dictionary<string, long> AccountCounts { get; init; } = new();
    public Dictionary<string, long> FolderCounts { get; init; } = new();
}
STATS_EOF
echo "  ✓ Data/IndexStatistics.cs (created)"
fi

# =============================================================================
# Add GetStatisticsAsync to SearchDatabase if missing
# =============================================================================
if ! grep -q "GetStatisticsAsync" "$SEARCH_DIR/Data/SearchDatabase.cs" 2>/dev/null; then
    echo ""
    echo "⚠️  Note: You may need to add GetStatisticsAsync method to SearchDatabase.cs"
    echo "   See the method implementation below:"
    echo ""
    cat << 'STATS_METHOD'
    /// <summary>
    /// Gets statistics about the search index.
    /// </summary>
    public async Task<IndexStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var stats = new IndexStatistics();

        // Total emails
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM emails";
            stats = stats with { TotalEmails = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) };
        }

        // Unique senders
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT from_address) FROM emails WHERE from_address IS NOT NULL";
            stats = stats with { UniqueSenders = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) };
        }

        // Date range
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT MIN(date_sent_unix), MAX(date_sent_unix) FROM emails WHERE date_sent_unix IS NOT NULL";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && !reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                stats = stats with
                {
                    OldestEmail = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                    NewestEmail = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1))
                };
            }
        }

        // Emails with attachments
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM emails WHERE has_attachments = 1";
            stats = stats with { EmailsWithAttachments = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) };
        }

        // Account counts
        var accountCounts = new Dictionary<string, long>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT account, COUNT(*) as cnt FROM emails WHERE account IS NOT NULL GROUP BY account";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                accountCounts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        stats = stats with { AccountCounts = accountCounts };

        // Folder counts
        var folderCounts = new Dictionary<string, long>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT folder, COUNT(*) as cnt FROM emails WHERE folder IS NOT NULL GROUP BY folder";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                folderCounts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        stats = stats with { FolderCounts = folderCounts };

        return stats;
    }
STATS_METHOD
fi

echo ""
echo "✅ All command files updated successfully!"
echo ""
echo "Key changes made:"
echo "  • Changed Option aliases from collection expressions ['--opt', '-o']"
echo "    to explicit arrays: new[] { '--opt', '-o' }"
echo "  • All commands use SetAction with (parseResult, ct) signature"
echo "  • Global options passed to subcommand Create() methods"
echo "  • Proper async/await patterns throughout"
echo ""
echo "Run 'dotnet build' to verify the fixes.
