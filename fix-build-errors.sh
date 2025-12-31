#!/bin/bash
# Fix build errors in MyEmailSearch
# Run from the repository root: ~/src/dotnet/MyImapDownloader

set -e

cd ~/src/dotnet/MyImapDownloader

echo "=== Fixing MyEmailSearch build errors ==="

# Fix 1: Program.cs - Use params syntax for Option aliases (not array)
echo "Fixing Program.cs..."
cat > MyEmailSearch/Program.cs << 'PROGRAMCS'
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
        var rootCommand = new RootCommand("MyEmailSearch - Full-text search for email archives");

        // Global options - use params syntax for aliases (name first, then aliases)
        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory"
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "Path to the search index database"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        // Add subcommands
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
PROGRAMCS

# Fix 2: IndexCommand.cs - Use params syntax
echo "Fixing IndexCommand.cs..."
cat > MyEmailSearch/Commands/IndexCommand.cs << 'INDEXCMD'
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
        var fullOption = new Option<bool>("--full", "-f")
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
        Console.WriteLine($"Database location:    {databasePath}");
        Console.WriteLine($"Mode:                 {(full ? "Full rebuild" : "Incremental")}");
        Console.WriteLine($"Index content:        {(content ? "Yes" : "No")}");
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
INDEXCMD

# Fix 3: RebuildCommand.cs - Use params syntax
echo "Fixing RebuildCommand.cs..."
cat > MyEmailSearch/Commands/RebuildCommand.cs << 'REBUILDCMD'
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
        var confirmOption = new Option<bool>("--yes", "-y")
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
REBUILDCMD

# Fix 4: SearchCommand.cs - Use params syntax
echo "Fixing SearchCommand.cs..."
cat > MyEmailSearch/Commands/SearchCommand.cs << 'SEARCHCMD'
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

        var limitOption = new Option<int>("--limit", "-l")
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>("--format", "-f")
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

        await database.InitializeAsync(ct);

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
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 28);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 48);

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
SEARCHCMD

# Fix 5: StatusCommand.cs - Simplified version using existing methods only
echo "Fixing StatusCommand.cs..."
cat > MyEmailSearch/Commands/StatusCommand.cs << 'STATUSCMD'
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
        var command = new Command("status", "Show index status and statistics");

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
        Console.WriteLine("MyEmailSearch - Index Status");
        Console.WriteLine(new string('=', 40));
        Console.WriteLine();

        Console.WriteLine($"Archive path:  {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine();

        if (!File.Exists(databasePath))
        {
            Console.WriteLine("Status: No index exists yet");
            Console.WriteLine("Run 'myemailsearch index' to create the index");
            return;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();

        try
        {
            await database.InitializeAsync(ct);

            var emailCount = await database.GetEmailCountAsync(ct);
            var dbSize = database.GetDatabaseSize();
            var lastIndexed = await database.GetMetadataAsync("last_indexed_time", ct);
            var lastIndexedTime = lastIndexed != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(lastIndexed))
                : (DateTimeOffset?)null;

            Console.WriteLine($"Total emails indexed: {emailCount:N0}");
            Console.WriteLine($"Index size:           {FormatBytes(dbSize)}");
            Console.WriteLine($"Last indexed:         {lastIndexedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");

            var healthy = await database.IsHealthyAsync(ct);
            Console.WriteLine($"Database health:      {(healthy ? "OK" : "ERROR")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading database: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {suffixes[i]}";
    }
}
STATUSCMD

echo ""
echo "=== All files fixed! ==="
echo ""
echo "Running build..."
dotnet build MyEmailSearch/MyEmailSearch.csproj
