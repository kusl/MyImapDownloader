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
