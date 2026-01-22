namespace MyEmailSearch.Data;

/// <summary>
/// Statistics about the database.
/// </summary>
public sealed record DatabaseStatistics
{
    public long TotalEmails { get; init; }
    public long HeaderIndexed { get; init; }
    public long ContentIndexed { get; init; }
    public long FtsIndexSize { get; init; }
    public Dictionary<string, long> AccountCounts { get; init; } = new();
    public Dictionary<string, long> FolderCounts { get; init; } = new();
}
