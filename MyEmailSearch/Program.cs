using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;

namespace MyEmailSearch;

/// <summary>
/// MyEmailSearch - Email Archive Search Utility
/// 
/// A companion tool for MyImapDownloader that enables fast searching
/// across archived emails using SQLite FTS5 full-text search.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build the root command with subcommands
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive")
        {
            SearchCommand.Create(),
            IndexCommand.Create(),
            StatusCommand.Create(),
            RebuildCommand.Create()
        };

        // Add global options
        var archiveOption = new Option<string?>(
            aliases: ["--archive", "-a"],
            description: "Path to the email archive directory");
        
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        rootCommand.AddGlobalOption(archiveOption);
        rootCommand.AddGlobalOption(verboseOption);

        return await rootCommand.InvokeAsync(args);
    }
}
