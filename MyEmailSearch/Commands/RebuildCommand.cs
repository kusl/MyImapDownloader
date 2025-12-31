using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'rebuild' command to rebuild the index from scratch.
/// </summary>
public static class RebuildCommand
{
    public static Command Create()
    {
        var confirmOption = new Option<bool>(
            aliases: ["--yes", "-y"],
            description: "Skip confirmation prompt");

        var command = new Command("rebuild", "Rebuild the entire search index from scratch")
        {
            confirmOption
        };

        command.SetHandler(async (confirm, ct) =>
        {
            await ExecuteAsync(confirm, ct);
        }, confirmOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(bool confirm, CancellationToken ct)
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

        // TODO: Implement rebuild logic
        await Task.CompletedTask;
    }
}
