using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'index' command for building/updating the search index.
/// </summary>
public static class IndexCommand
{
    public static Command Create()
    {
        var fullOption = new Option<bool>(
            aliases: ["--full"],
            description: "Perform full reindex instead of incremental update");

        var contentOption = new Option<bool>(
            aliases: ["--content"],
            description: "Also index email body content (slower but enables full-text search)");

        var command = new Command("index", "Build or update the search index")
        {
            fullOption,
            contentOption
        };

        command.SetHandler(async (full, content, ct) =>
        {
            await ExecuteAsync(full, content, ct);
        }, fullOption, contentOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(
        bool full,
        bool content,
        CancellationToken ct)
    {
        Console.WriteLine($"Indexing... Full: {full}, Content: {content}");

        // TODO: Implement indexing logic
        await Task.CompletedTask;
    }
}
