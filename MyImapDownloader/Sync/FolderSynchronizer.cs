using Microsoft.Extensions.Logging;

using MyImapDownloader.Archive;
using MyImapDownloader.Imap;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Sync;

public sealed class FolderSynchronizer(
    EmailArchive archive,
    IMailServer server,
    ILogger<FolderSynchronizer> logger,
    TimeProvider time)
{
    private const int BatchSize = 50;

    public async Task SyncAsync(IRemoteFolder folder, DownloadOptions options, CancellationToken ct)
    {
        using var activity = DownloaderTelemetry.ActivitySource.StartActivity("ProcessFolder");
        activity?.SetTag("folder", folder.FullName);

        try
        {
            var uidValidity = await folder.OpenAsync(ct);
            var useCursor = !options.HasDateWindow;
            var lastUid = useCursor ? await archive.GetLastUidAsync(folder.FullName, uidValidity, ct) : 0;

            logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, lastUid);

            var uids = await folder.FindAsync(new MessageWindow((uint)lastUid, options.StartDate, options.EndDate), ct);
            logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            var checkpoint = new FolderCheckpointTracker();

            foreach (var batch in uids.Chunk(BatchSize))
            {
                ct.ThrowIfCancellationRequested();

                var failed = await DownloadBatchAsync(folder, batch, checkpoint, ct);

                if (useCursor && checkpoint.SafeCheckpoint > 0)
                {
                    await archive.SetLastUidAsync(
                        folder.FullName, checkpoint.SafeCheckpoint, uidValidity, CancellationToken.None);
                }

                if (failed.Count > 0)
                {
                    logger.LogWarning(
                        "Failed to download {Count} emails in {Folder}: UIDs {Uids}",
                        failed.Count, folder.FullName, string.Join(", ", failed));
                }
            }

            if (checkpoint.LowestFailedUid is { } lowestFailed)
            {
                logger.LogWarning(
                    "Checkpoint for {Folder} held at UID {Checkpoint} so UID {Failed} is retried next run",
                    folder.FullName, checkpoint.SafeCheckpoint, lowestFailed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Error processing folder {Folder}", folder.FullName);
            throw;
        }
    }

    private async Task<List<uint>> DownloadBatchAsync(
        IRemoteFolder folder,
        IReadOnlyList<uint> uids,
        FolderCheckpointTracker checkpoint,
        CancellationToken ct)
    {
        List<uint> failed = [];

        foreach (var message in await folder.DescribeAsync(uids, ct))
        {
            using var activity = DownloaderTelemetry.ActivitySource.StartActivity("ProcessEmail");

            if (!string.IsNullOrWhiteSpace(message.MessageId) && await archive.ContainsAsync(message.MessageId, ct))
            {
                logger.LogDebug("Skipping duplicate {Id}", message.MessageId);
                checkpoint.MarkProcessed(message.Uid);
                continue;
            }

            if (await TryDownloadAsync(folder, message, ct))
            {
                checkpoint.MarkProcessed(message.Uid);
            }
            else
            {
                failed.Add(message.Uid);
                checkpoint.MarkFailed(message.Uid);
            }
        }

        return failed;
    }

    private async Task<bool> TryDownloadAsync(IRemoteFolder folder, RemoteMessage message, CancellationToken ct)
    {
        Stream content;
        try
        {
            content = await folder.ReadAsync(message.Uid, ct);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && server.Classify(exception) != MailFailure.Connection)
        {
            logger.LogError(exception, "Failed to download UID {Uid}", message.Uid);
            return false;
        }

        await using (content)
        {
            try
            {
                var outcome = await archive.StoreAsync(
                    content, message.MessageId, message.InternalDate ?? time.GetUtcNow(), folder.FullName, ct);

                if (outcome is Stored)
                {
                    DownloaderTelemetry.EmailsDownloaded.Add(1);
                    logger.LogInformation("Downloaded: {Subject}", message.Subject);
                }

                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to download UID {Uid}", message.Uid);
                return false;
            }
        }
    }
}
