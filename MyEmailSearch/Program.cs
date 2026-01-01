using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MyEmailSearch - Full-text search for email archives");

        // Global options - these will be inherited by all subcommands
        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory"
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "Path to the search index database"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        // Use AddGlobalOption so subcommands inherit these options
        rootCommand.AddGlobalOption(archiveOption);
        rootCommand.AddGlobalOption(databaseOption);
        rootCommand.AddGlobalOption(verboseOption);

        // Add subcommands - pass the global options so they can access them
        rootCommand.AddCommand(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.AddCommand(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.AddCommand(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.AddCommand(StatusCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Creates the DI service provider with all required services.
    /// </summary>
    public static ServiceProvider CreateServiceProvider(
        string archivePath,
        string databasePath,
        bool verbose)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Configuration
        services.AddSingleton(new SearchConfiguration
        {
            ArchivePath = archivePath,
            DatabasePath = databasePath
        });

        // Core services
        services.AddSingleton<SearchDatabase>();
        services.AddSingleton<ArchiveScanner>();
        services.AddSingleton<EmailParser>();
        services.AddSingleton<IndexManager>();
        services.AddSingleton<QueryParser>();
        services.AddSingleton<SearchEngine>();
        services.AddSingleton<SnippetGenerator>();

        return services.BuildServiceProvider();
    }
}
