using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MyEmailSearch.Commands;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive");

        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory",
            Recursive = true
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "Path to the search database file",
            Recursive = true
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output",
            Recursive = true
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    public static ServiceProvider CreateServiceProvider(
        string archivePath,
        string databasePath,
        bool verbose)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        services.AddSingleton<QueryParser>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

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
