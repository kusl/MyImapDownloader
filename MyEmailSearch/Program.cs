using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

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

        // Add global options
        var archiveOption = new Option<string?>(["--archive", "-a"])
        {
            Description = "Path to the email archive directory"
        };

        var verboseOption = new Option<bool>(["--verbose", "-v"])
        {
            Description = "Enable verbose output"
        };

        var databaseOption = new Option<string?>(["--database", "-d"])
        {
            Description = "Path to the search index database"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(databaseOption);

        // Add subcommands
        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Creates a configured service provider for dependency injection.
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

        // Database
        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        // Search components
        services.AddSingleton<QueryParser>();
        services.AddSingleton<SnippetGenerator>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<SnippetGenerator>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

        // Indexing components
        services.AddSingleton(sp =>
            new ArchiveScanner(sp.GetRequiredService<ILogger<ArchiveScanner>>()));
        services.AddSingleton(sp =>
            new EmailParser(archivePath, sp.GetRequiredService<ILogger<EmailParser>>()));
        services.AddSingleton(sp => new IndexManager(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<ArchiveScanner>(),
            sp.GetRequiredService<EmailParser>(),
            sp.GetRequiredService<ILogger<IndexManager>>()));

        return services.BuildServiceProvider();
    }
}
