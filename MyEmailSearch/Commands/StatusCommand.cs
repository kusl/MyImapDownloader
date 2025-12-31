using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'status' command to show index statistics.
/// </summary>
public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show index status and statistics");

        command.SetHandler(async (ct) =>
        {
            await ExecuteAsync(ct);
        }, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("Index Status:");
        Console.WriteLine("=============");
        Console.WriteLine("  Total emails indexed: (not yet implemented)");
        Console.WriteLine("  Index size: (not yet implemented)");
        Console.WriteLine("  Last updated: (not yet implemented)");

        await Task.CompletedTask;
    }
}
