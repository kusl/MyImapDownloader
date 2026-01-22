using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Manages the email search index lifecycle.
/// </summary>
public sealed class IndexManager(
    SearchDatabase database,
    ArchiveScanner scanner,
    EmailParser parser,
    ILogger<IndexManager> logger)
{
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

        logger.LogInformation("Starting smart incremental index of {Path}", archivePath);

        // Load map of existing files and their timestamps
        var knownFiles = await database.GetKnownFilesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Loaded {Count} existing file records from database", knownFiles.Count);

        var emailFiles = scanner.ScanForEmails(archivePath).ToList();
        var batch = new List<EmailDocument>();
        var processed = 0;
        var total = emailFiles.Count;

        foreach (var file in emailFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(file);

                // Smart Scan Check:
                // If the file path exists in DB AND the last modified time matches exact ticks,
                // we skip it entirely. This prevents parsing.
                if (knownFiles.TryGetValue(file, out var existingTicks) &&
                    existingTicks == fileInfo.LastWriteTimeUtc.Ticks)
                {
                    result.Skipped++;
                    processed++;
                    progress?.Report(new IndexingProgress(processed, total, file));
                    continue;
                }

                // Parse the email
                var doc = await parser.ParseAsync(file, includeContent, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    batch.Add(doc);
                    result.Indexed++;
                }

                // Batch insert
                if (batch.Count >= 100)
                {
                    await database.UpsertEmailsAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to index {File}", file);
                result.Errors++;
            }

            processed++;
            progress?.Report(new IndexingProgress(processed, total, file));
        }

        // Insert remaining batch
        if (batch.Count > 0)
        {
            await database.UpsertEmailsAsync(batch, ct).ConfigureAwait(false);
        }

        // Update metadata
        await database.SetMetadataAsync("last_indexed_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ct).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        logger.LogInformation(
            "Indexing complete: {Indexed} indexed, {Skipped} skipped, {Errors} errors in {Duration}",
            result.Indexed, result.Skipped, result.Errors, result.Duration);

        return result;
    }

    /// <summary>
    /// Rebuilds the entire index from scratch.
    /// </summary>
    public async Task<IndexingResult> RebuildIndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogWarning("Starting full index rebuild - this will delete all existing data");

        // Clear existing data and reinitialize
        await database.ClearAllDataAsync(ct).ConfigureAwait(false);
        await database.InitializeAsync(ct).ConfigureAwait(false);

        // Run full index
        return await IndexAsync(archivePath, includeContent, progress, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Result of an indexing operation.
/// </summary>
public sealed class IndexingResult
{
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Progress report for indexing operations.
/// </summary>
public sealed record IndexingProgress(int Processed, int Total, string? CurrentFile = null)
{
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}
