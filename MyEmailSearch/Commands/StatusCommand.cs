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
