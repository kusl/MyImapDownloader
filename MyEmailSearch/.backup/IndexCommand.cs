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
        var fullOption = new Option<bool>("--full")
        {
            Description = "Perform full reindex instead of incremental update"
        };

        var contentOption = new Option<bool>("--content")
        {
            Description = "Also index email body content (slower but enables full-text search)"
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
        Console.WriteLine($"Archive path: {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine($"Mode: {(full ? "Full rebuild" : "Incremental")}");
        Console.WriteLine($"Index content: {content}");
        Console.WriteLine();

        if (!Directory.Exists(archivePath))
        {
            Console.WriteLine($"Error: Archive path does not exist: {archivePath}");
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

        // Create progress reporter
        var progress = new Progress<IndexingProgress>(p =>
        {
            Console.Write($"\rProcessing: {p.Processed}/{p.Total} ({p.Percentage:F1}%)");
        });

        // Run indexing
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
        Console.WriteLine($"  Indexed: {result.Indexed}");
        Console.WriteLine($"  Skipped: {result.Skipped}");
        Console.WriteLine($"  Errors:  {result.Errors}");
        Console.WriteLine($"  Time:    {result.Duration}");
    }
}
