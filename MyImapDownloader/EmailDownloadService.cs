// MyImapDownloader/EmailDownloadService.cs
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Logging;

using MyImapDownloader.Telemetry;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace MyImapDownloader;

public class EmailDownloadService
{
    private readonly ILogger<EmailDownloadService> _logger;
    private readonly ImapConfiguration _config;
    private readonly EmailStorageService _storage;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public EmailDownloadService(
        ILogger<EmailDownloadService> logger,
        ImapConfiguration config,
        EmailStorageService storage)
    {
        _logger = logger;
        _config = config;
        _storage = storage;

        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not AuthenticationException)
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 300)),
                (exception, retryCount, timeSpan) =>
                {
                    _logger.LogWarning("Retry {Count} in {Delay}: {Message}", retryCount, timeSpan, exception.Message);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not AuthenticationException)
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(2));
    }

    public async Task DownloadEmailsAsync(DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("DownloadEmails");

        await _storage.InitializeAsync(ct);

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async () =>
        {
            using var client = new ImapClient { Timeout = 180_000 };
            try
            {
                await ConnectAndAuthenticateAsync(client, ct);

                // After AuthenticateAsync, MailKit's contract is that Inbox is available.
                // We resolve it once and fail loudly if that invariant is ever violated,
                // rather than implicitly NullRef'ing later or suppressing the analyzer.
                var inbox = client.Inbox
                    ?? throw new InvalidOperationException(
                        "IMAP client returned a null Inbox after authentication.");

                var folders = options.AllFolders
                    ? await GetAllFoldersAsync(client, ct)
                    : new List<IMailFolder> { inbox };

                foreach (var folder in folders)
                {
                    await ProcessFolderAsync(folder, options, ct);
                }
            }
            finally
            {
                if (client.IsConnected) await client.DisconnectAsync(true, ct);
            }
        });
    }

    private async Task ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessFolder");
        activity?.SetTag("folder", folder.FullName);

        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

            long lastUidVal = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity, ct);
            UniqueId? startUid = lastUidVal > 0 ? new UniqueId((uint)lastUidVal) : null;

            _logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, startUid);

            var query = SearchQuery.All;
            if (startUid.HasValue)
            {
                var range = new UniqueIdRange(new UniqueId(startUid.Value.Id + 1), UniqueId.MaxValue);
                query = SearchQuery.Uids(range);
            }
            if (options.StartDate.HasValue) query = query.And(SearchQuery.DeliveredAfter(options.StartDate.Value));
            if (options.EndDate.HasValue) query = query.And(SearchQuery.DeliveredBefore(options.EndDate.Value));

            var uids = await folder.SearchAsync(query, ct);
            _logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            int batchSize = 50;
            for (int i = 0; i < uids.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested) break;

                var batch = uids.Skip(i).Take(batchSize).ToList();
                var result = await DownloadBatchAsync(folder, batch, ct);

                // FIX: Only update checkpoint to the SAFE point
                // If there were failures, don't advance past the lowest failed UID
                if (result.SafeCheckpointUid > 0)
                {
                    await _storage.UpdateLastUidAsync(folder.FullName, result.SafeCheckpointUid, folder.UidValidity, ct);
                }

                // FIX: Log failed UIDs for manual intervention if needed
                if (result.FailedUids.Count > 0)
                {
                    _logger.LogWarning("Failed to download {Count} emails in {Folder}: UIDs {Uids}",
                        result.FailedUids.Count, folder.FullName, string.Join(", ", result.FailedUids));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing folder {Folder}", folder.FullName);
            throw;
        }
    }

    /// <summary>
    /// FIX: New result type to track both successful and failed UIDs.
    /// </summary>
    private sealed record BatchResult(long SafeCheckpointUid, List<uint> FailedUids);

    private async Task<BatchResult> DownloadBatchAsync(IMailFolder folder, IList<UniqueId> uids, CancellationToken ct)
    {
        long safeCheckpointUid = 0;
        var failedUids = new List<uint>();
        long? lowestFailedUid = null;

        var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        foreach (var item in items)
        {
            using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessEmail");

            // Hoist the envelope once. We requested MessageSummaryItems.Envelope above
            // so in practice it should always be non-null, but MailKit's API surfaces
            // it as nullable. A missing envelope means "no MessageId available", which
            // is handled identically to a blank MessageId via the NO-ID synthetic path.
            var envelope = item.Envelope;
            var envelopeMessageId = envelope?.MessageId;

            string normalizedMessageIdentifier = string.IsNullOrWhiteSpace(envelopeMessageId)
                ? $"NO-ID-{item.InternalDate?.Ticks ?? DateTime.UtcNow.Ticks}-{Guid.NewGuid()}"
                : EmailStorageService.NormalizeMessageId(envelopeMessageId);

            if (await _storage.ExistsAsyncNormalized(normalizedMessageIdentifier, ct))
            {
                _logger.LogDebug("Skipping duplicate {Id}", normalizedMessageIdentifier);
                // FIX: Even duplicates count as successfully processed for checkpoint
                if (lowestFailedUid == null || item.UniqueId.Id < lowestFailedUid)
                {
                    safeCheckpointUid = Math.Max(safeCheckpointUid, (long)item.UniqueId.Id);
                }
                continue;
            }

            try
            {
                using var stream = await folder.GetStreamAsync(item.UniqueId, ct);
                bool isNew = await _storage.SaveStreamAsync(
                    stream,
                    envelopeMessageId ?? string.Empty,
                    item.InternalDate ?? DateTimeOffset.UtcNow,
                    folder.FullName,
                    ct);

                if (isNew) _logger.LogInformation("Downloaded: {Subject}", envelope?.Subject);

                // FIX: Only update safe checkpoint if no failures before this UID
                if (lowestFailedUid == null || item.UniqueId.Id < lowestFailedUid)
                {
                    safeCheckpointUid = Math.Max(safeCheckpointUid, (long)item.UniqueId.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
                failedUids.Add(item.UniqueId.Id);

                // FIX: Track the lowest failed UID
                if (lowestFailedUid == null || item.UniqueId.Id < lowestFailedUid)
                {
                    lowestFailedUid = item.UniqueId.Id;
                }

                // FIX: Adjust safe checkpoint to be just before the first failure
                if (lowestFailedUid.HasValue && safeCheckpointUid >= lowestFailedUid.Value)
                {
                    safeCheckpointUid = lowestFailedUid.Value - 1;
                }
            }
        }

        return new BatchResult(safeCheckpointUid, failedUids);
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);
        await client.ConnectAsync(_config.Server, _config.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_config.Username, _config.Password, ct);
    }

    private async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, CancellationToken ct)
    {
        var folders = new List<IMailFolder>();
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        await CollectFoldersRecursiveAsync(personal, folders, ct);

        // After authentication, client.Inbox is expected to be non-null. If MailKit
        // ever surfaces it as null in the all-folders code path, skip the prepend
        // rather than crash — other folders are still processable.
        var inbox = client.Inbox;
        if (inbox is not null && !folders.Contains(inbox))
        {
            folders.Insert(0, inbox);
        }

        return folders;
    }

    private async Task CollectFoldersRecursiveAsync(IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
    {
        foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
        {
            folders.Add(folder);
            await CollectFoldersRecursiveAsync(folder, folders, ct);
        }
    }
}
