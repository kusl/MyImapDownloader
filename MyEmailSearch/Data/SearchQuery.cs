namespace MyEmailSearch.Data;

/// <summary>
/// Represents parsed search criteria.
/// </summary>
public sealed record SearchQuery
{
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
    public string? Subject { get; init; }
    public string? ContentTerms { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? Account { get; init; }
    public string? Folder { get; init; }
    public int Skip { get; init; } = 0;
    public int Take { get; init; } = 100;
    public SearchSortOrder SortOrder { get; init; } = SearchSortOrder.DateDescending;
}

public enum SearchSortOrder
{
    DateDescending,
    DateAscending,
    Relevance
}
