using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine(
    SearchDatabase database,
    QueryParser queryParser,
    SnippetGenerator snippetGenerator,
    ILogger<SearchEngine> logger)
{
    private readonly SearchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly QueryParser _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
    private readonly SnippetGenerator _snippetGenerator = snippetGenerator ?? throw new ArgumentNullException(nameof(snippetGenerator));
    private readonly ILogger<SearchEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes a search query string and returns results.
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

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        return await SearchAsync(query, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a parsed search query and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", FormatQueryForLog(query));

        // Execute the search query (with LIMIT)
        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

        // FIX: Get actual total count (without LIMIT) for accurate pagination
        var totalCount = await _database.GetTotalCountForQueryAsync(query, ct).ConfigureAwait(false);

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
            "Search completed: {ResultCount} results returned, {TotalCount} total matches in {ElapsedMs}ms",
            results.Count, totalCount, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = totalCount,
            Skip = query.Skip,
            Take = query.Take,
            QueryTime = stopwatch.Elapsed
        };
    }

    private static string FormatQueryForLog(SearchQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.FromAddress)) parts.Add($"from:{query.FromAddress}");
        if (!string.IsNullOrWhiteSpace(query.ToAddress)) parts.Add($"to:{query.ToAddress}");
        if (!string.IsNullOrWhiteSpace(query.Subject)) parts.Add($"subject:{query.Subject}");
        if (!string.IsNullOrWhiteSpace(query.ContentTerms)) parts.Add(query.ContentTerms);
        if (!string.IsNullOrWhiteSpace(query.Account)) parts.Add($"account:{query.Account}");
        if (!string.IsNullOrWhiteSpace(query.Folder)) parts.Add($"folder:{query.Folder}");
        return string.Join(" ", parts);
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
