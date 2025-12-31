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
