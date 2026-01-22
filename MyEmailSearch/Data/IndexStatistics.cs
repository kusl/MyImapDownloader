using System;
using System.Collections.Generic;

namespace MyEmailSearch.Data;

/// <summary>
/// Statistics about the search index.
/// </summary>
public sealed record IndexStatistics
{
    public long TotalEmails { get; init; }
    public long UniqueSenders { get; init; }
    public DateTimeOffset OldestEmail { get; init; }
    public DateTimeOffset NewestEmail { get; init; }
    public long EmailsWithAttachments { get; init; }
    public Dictionary<string, long> AccountCounts { get; init; } = new();
    public Dictionary<string, long> FolderCounts { get; init; } = new();
}
