namespace MyEmailSearch.Data;

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
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
}
