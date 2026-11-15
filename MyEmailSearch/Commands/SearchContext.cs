using System.CommandLine;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch.Commands;

public sealed record SearchSettings(string ArchivePath, string DatabasePath, bool Verbose);

public sealed class GlobalOptions
{
    public Option<string?> Archive { get; } = new("--archive", "-a")
    {
        Description = "Path to the email archive directory",
        Recursive = true
    };

    public Option<string?> Database { get; } = new("--database", "-d")
    {
        Description = "Path to the search database file",
        Recursive = true
    };

    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Enable verbose output",
        Recursive = true
    };

    public void AddTo(Command command)
    {
        command.Add(Archive);
        command.Add(Database);
        command.Add(Verbose);
    }

    public SearchSettings Resolve(ParseResult parseResult) => new(
        parseResult.GetValue(Archive) ?? SearchPaths.DefaultArchive(),
        parseResult.GetValue(Database) ?? SearchPaths.DefaultDatabase(),
        parseResult.GetValue(Verbose));
}

public sealed class SearchContext : IAsyncDisposable
{
    private readonly ILoggerFactory _loggers;

    private SearchContext(ILoggerFactory loggers, SearchDatabase database)
    {
        _loggers = loggers;
        Database = database;
        Engine = new SearchEngine(database, loggers.CreateLogger<SearchEngine>());
        Indexer = new IndexManager(
            database,
            new EmailParser(loggers.CreateLogger<EmailParser>(), TimeProvider.System),
            loggers.CreateLogger<IndexManager>(),
            TimeProvider.System);
    }

    public SearchDatabase Database { get; }

    public SearchEngine Engine { get; }

    public IndexManager Indexer { get; }

    public static async Task<SearchContext> OpenAsync(SearchSettings settings, CancellationToken ct)
    {
        var loggers = LoggerFactory.Create(logging =>
        {
            logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
            logging.SetMinimumLevel(settings.Verbose ? LogLevel.Debug : LogLevel.Information);
        });

        try
        {
            return new SearchContext(loggers, await SearchDatabase.OpenAsync(settings.DatabasePath, ct));
        }
        catch
        {
            loggers.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        _loggers.Dispose();
    }
}
