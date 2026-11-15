using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

public sealed record IndexingResult(int Indexed, int Skipped, int Errors, TimeSpan Duration);

public sealed record IndexingProgress(int Processed, int Total, string? CurrentFile = null)
{
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}

public sealed class IndexManager(
    SearchDatabase database,
    EmailParser parser,
    ILogger<IndexManager> logger,
    TimeProvider time)
{
    private const int BatchSize = 100;

    public async Task<IndexingResult> IndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("Starting incremental index of {Path}", archivePath);

        if (!Directory.Exists(archivePath))
        {
            logger.LogWarning("Archive path does not exist: {Path}", archivePath);
        }

        var known = await database.GetKnownFilesAsync(ct);
        var files = ArchiveScanner.FindEmails(archivePath).ToList();
        logger.LogInformation("Found {Files} emails; {Known} already indexed", files.Count, known.Count);

        List<EmailDocument> batch = [with(capacity: BatchSize)];
        var (indexed, skipped, errors, processed) = (0, 0, 0, 0);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (IsUnchanged(file, known))
            {
                skipped++;
            }
            else if (await parser.ParseAsync(file, archivePath, includeContent, ct) is { } document)
            {
                batch.Add(document);
                indexed++;
            }
            else
            {
                errors++;
            }

            if (batch.Count >= BatchSize)
            {
                await database.UpsertAsync(batch, ct);
                batch.Clear();
            }

            progress?.Report(new IndexingProgress(++processed, files.Count, file));
        }

        await database.UpsertAsync(batch, ct);
        await database.SetMetadataAsync(
            SearchDatabase.LastIndexedKey,
            time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ct);

        var result = new IndexingResult(indexed, skipped, errors, Stopwatch.GetElapsedTime(started));
        logger.LogInformation(
            "Indexing complete: {Indexed} indexed, {Skipped} skipped, {Errors} errors in {Duration}",
            result.Indexed, result.Skipped, result.Errors, result.Duration);
        return result;
    }

    public async Task<IndexingResult> RebuildAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogWarning("Starting full index rebuild; all indexed data is discarded first");
        await database.ResetAsync(ct);
        return await IndexAsync(archivePath, includeContent, progress, ct);
    }

    private static bool IsUnchanged(string file, Dictionary<string, long> known) =>
        known.TryGetValue(file, out var ticks) && ticks == File.GetLastWriteTimeUtc(file).Ticks;
}
