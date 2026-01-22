namespace MyEmailSearch.Data;

/// <summary>
/// Represents a single search result with optional snippet.
/// </summary>
public sealed record SearchResult
{
    public required EmailDocument Email { get; init; }
    public string? Snippet { get; init; }
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
    public double? Score { get; init; }
}

/// <summary>
/// Represents a set of search results with pagination info.
/// </summary>
public sealed record SearchResultSet
{
    public IReadOnlyList<SearchResult> Results { get; init; } = [];
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public TimeSpan QueryTime { get; init; }

    public bool HasMore => Skip + Results.Count < TotalCount;
}
