using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

public sealed class SearchEngine(SearchDatabase database, ILogger<SearchEngine> logger)
{
    public async Task<SearchResultSet> SearchAsync(
        string queryText,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return new SearchResultSet { Skip = offset, Take = limit };
        }

        var started = Stopwatch.GetTimestamp();
        var query = QueryParser.Parse(queryText) with { Take = limit, Skip = offset };
        logger.LogDebug("Executing search: {Query}", query);

        var emails = await database.QueryAsync(query, ct);
        var totalCount = await database.CountAsync(query, ct);

        IReadOnlyList<string> terms = query.ContentTerms?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var results = emails
            .Select(email => new SearchResult
            {
                Email = email,
                Snippet = terms.Count > 0
                    ? SnippetGenerator.Generate(email.BodyText ?? email.BodyPreview, query.ContentTerms)
                    : email.BodyPreview,
                MatchedTerms = terms
            })
            .ToList();

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.LogDebug(
            "Search completed: {ResultCount} results returned, {TotalCount} total matches in {ElapsedMs}ms",
            results.Count, totalCount, (long)elapsed.TotalMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = totalCount,
            Skip = query.Skip,
            Take = query.Take,
            QueryTime = elapsed
        };
    }
}
