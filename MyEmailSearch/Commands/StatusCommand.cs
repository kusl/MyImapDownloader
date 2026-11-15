using System.CommandLine;
using System.Globalization;

using Microsoft.Data.Sqlite;

using MyEmailSearch.Data;

namespace MyEmailSearch.Commands;

public static class StatusCommand
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static Command Create(GlobalOptions global)
    {
        var command = new Command("status", "Show index status and statistics");

        command.SetAction(async (parseResult, ct) =>
        {
            var settings = global.Resolve(parseResult);
            var output = parseResult.InvocationConfiguration.Output;

            output.WriteLine("MyEmailSearch - Index Status");
            output.WriteLine(new string('=', 40));
            output.WriteLine();
            output.WriteLine($"Archive path:  {settings.ArchivePath}");
            output.WriteLine($"Database path: {settings.DatabasePath}");
            output.WriteLine();

            if (!File.Exists(settings.DatabasePath))
            {
                output.WriteLine("Status: No index exists yet");
                output.WriteLine("Run 'myemailsearch index' to create the index");
                return;
            }

            try
            {
                await using var context = await SearchContext.OpenAsync(settings, ct);
                var database = context.Database;
                var lastIndexed = await database.GetMetadataAsync(SearchDatabase.LastIndexedKey, ct);

                output.WriteLine($"Total emails indexed: {await database.GetEmailCountAsync(ct):N0}");
                output.WriteLine($"Index size:           {FormatBytes(database.SizeBytes)}");
                output.WriteLine($"Last indexed:         {FormatTimestamp(lastIndexed)}");
                output.WriteLine($"Database health:      {(await database.IsHealthyAsync(ct) ? "OK" : "ERROR")}");
            }
            catch (SqliteException exception)
            {
                output.WriteLine($"Error reading database: {exception.Message}");
            }
        });

        return command;
    }

    public static string FormatBytes(long bytes)
    {
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F2} {Units[unit]}";
    }

    private static string FormatTimestamp(string? unixSeconds) =>
        long.TryParse(unixSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss")
            : "Never";
}
