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
