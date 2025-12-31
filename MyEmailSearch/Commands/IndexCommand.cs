using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'index' command to build or update the search index.
/// </summary>
public static class IndexCommand
{
    public static Command Create()
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
            await ExecuteAsync(full, content, ct);
        });

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