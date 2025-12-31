using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'search' command for querying the email index.
/// </summary>
public static class SearchCommand
{
    public static Command Create()
    {
        var queryArgument = new Argument<string>(
            name: "query",
            description: "Search query (e.g., 'from:alice@example.com subject:report kafka')");

        var limitOption = new Option<int>(
            aliases: ["--limit", "-l"],
            getDefaultValue: () => 100,
            description: "Maximum number of results to return");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "table",
            description: "Output format: table, json, or csv");

        var command = new Command("search", "Search emails in the archive")
        {
            queryArgument,
            limitOption,
            formatOption
        };

        command.SetHandler(async (query, limit, format, ct) =>
        {
            await ExecuteAsync(query, limit, format, ct);
        }, queryArgument, limitOption, formatOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        CancellationToken ct)
    {
        Console.WriteLine($"Searching for: {query}");
        Console.WriteLine($"Limit: {limit}, Format: {format}");
        
        // TODO: Implement search logic
        await Task.CompletedTask;
    }
}
