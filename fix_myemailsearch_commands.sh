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

    private static string