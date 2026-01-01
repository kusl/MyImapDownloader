using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Manages the email search index lifecycle.
/// </summary>
public sealed class IndexManager
{
    private readonly SearchDatabase _database;
    private readonly ArchiveScanner _scanner;
    private readonly EmailParser _parser;
    private readonly ILogger<IndexManager> _logger;

    public IndexManager(
        SearchDatabase database,
        ArchiveScanner scanner,
        EmailParser parser,
        ILogger<IndexManager> logger)
    {
        _database = database;
        _scanner = scanner;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Performs incremental indexing - only indexes new or modified emails.
    /// </summary>
    public async Task<IndexingResult> IndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexingResult();

        _logger.LogInformation("Starting smart incremental index of {Path}", archivePath);

        // Load map of existing files and their timestamps
        var knownFiles = await _database.GetKnownFilesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Loaded {Count} existing file records from database", knownFiles.Count);

        var emailFiles = _scanner.ScanForEmails(archivePath);
        var batch = new List<EmailDocument>();
        var processed = 0;
        var total = emailFiles.Count();

        foreach (var file in emailFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(file);
                
                // Smart Scan Check:
                // If the file path exists in DB AND the last modified time matches exact ticks,
                // we skip it entirely. This prevents parsing.
                if (knownFiles.TryGetValue(file, out var storedTicks) && storedTicks == fileInfo.LastWriteTimeUtc.Ticks)
                {
                    result.Skipped++;
                }
                else
                {
                    // File is new OR modified
                    var email = await _parser.ParseAsync(file, includeContent, ct).ConfigureAwait(false);
                    if (email != null)
                    {
                        batch.Add(email);
                        result.Indexed++;

                        if (batch.Count >= 100)
                        {
                            await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse {File}", file);
                result.Errors++;
            }

            processed++;
            progress?.Report(new IndexingProgress
            {
                Processed = processed,
                Total = total,
                CurrentFile = file
            });
        }

        // Insert remaining batch
        if (batch.Count > 0)
        {
            await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
        }

        // Update last indexed time (purely informational now)
        await _database.SetMetadataAsync(
            "last_indexed_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ct).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Indexing complete: {Indexed} indexed, {Skipped} skipped, {Errors} errors in {Duration}",
            result.Indexed, result.Skipped, result.Errors, result.Duration);
            
        return result;
    }

    /// <summary>
    /// Performs a full reindex, deleting all existing data.
    /// </summary>
    public async Task<IndexingResult> RebuildIndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding entire index from scratch");
        await _database.RebuildAsync(ct).ConfigureAwait(false);
        return await IndexAsync(archivePath, includeContent, progress, ct).ConfigureAwait(false);
    }
}

public sealed record IndexingResult
{
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
}

public sealed record IndexingProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public string? CurrentFile { get; init; }
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}
