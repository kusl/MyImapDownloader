using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine
{
    private readonly SearchDatabase _database;
    private readonly QueryParser _queryParser;
    private readonly SnippetGenerator _snippetGenerator;
    private readonly ILogger<SearchEngine> _logger;

    public SearchEngine(
        SearchDatabase database,
        QueryParser queryParser,
        SnippetGenerator snippetGenerator,
        ILogger<SearchEngine> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _snippetGenerator = snippetGenerator ?? throw new ArgumentNullException(nameof(snippetGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a search query and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        string queryString,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return new SearchResultSet
            {
                Results = [],
                TotalCount = 0,
                Skip = offset,
                Take = limit,
                QueryTime = TimeSpan.Zero
            };
        }

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", queryString);

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var email in emails)
        {
            var snippet = !string.IsNullOrWhiteSpace(query.ContentTerms)
                ? _snippetGenerator.Generate(email.BodyText, query.ContentTerms)
                : email.BodyPreview;

            results.Add(new SearchResult
            {
                Email = email,
                Snippet = snippet,
                MatchedTerms = ExtractMatchedTerms(query)
            });
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Search completed: {ResultCount} results in {ElapsedMs}ms",
            results.Count, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = results.Count, // TODO: Get actual total count with separate count query
            Skip = offset,
            Take = limit,
            QueryTime = stopwatch.Elapsed
        };
    }

    private static IReadOnlyList<string> ExtractMatchedTerms(SearchQuery query)
    {
        var terms = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.ContentTerms))
        {
            terms.AddRange(query.ContentTerms.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return terms;
    }
}
