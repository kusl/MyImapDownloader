using System.CommandLine;
using MyEmailSearch.Commands;

namespace MyEmailSearch;

/// <summary>
/// Entry point for the MyEmailSearch CLI application.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build the root command with subcommands
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive");
        rootCommand.Subcommands.Add(SearchCommand.Create());
        rootCommand.Subcommands.Add(IndexCommand.Create());
        rootCommand.Subcommands.Add(StatusCommand.Create());
        rootCommand.Subcommands.Add(RebuildCommand.Create());

        // Add global options
        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(verboseOption);

        return await rootCommand.Parse(args).InvokeAsync();
    }
}