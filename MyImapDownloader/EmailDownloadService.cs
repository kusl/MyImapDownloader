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
    private const int BatchSize = 50;

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
            .Handle<Exception>(IsRetryable)
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 300)),
                (exception, retryCount, timeSpan) =>
                {
                    DiagnosticsConfig.RetryAttempts.Add(1);
                    _logger.LogWarning(exception, "Retry {Count} in {Delay}: {Message}", retryCount, timeSpan, exception.Message);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(IsRetryable)
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(2));
    }

    private static bool IsRetryable(Exception ex) =>
        ex is not AuthenticationException and not OperationCanceledException;

    public async Task DownloadEmailsAsync(DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("DownloadEmails");

        await _storage.InitializeAsync(ct);

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async token =>
        {
            using var client = new ImapClient { Timeout = 180_000 };
            try
            {
                await ConnectAndAuthenticateAsync(client, token);

                var inbox = client.Inbox
                    ?? throw new InvalidOperationException(
                        "IMAP client returned a null Inbox after authentication.");

                var folders = options.AllFolders
                    ? await GetAllFoldersAsync(client, inbox, token)
                    : new List<IMailFolder> { inbox };

                foreach (var folder in folders)
                {
                    await ProcessFolderAsync(folder, options, token);
                }
            }
            finally
            {
                await DisconnectQuietlyAsync(client);
            }
        }, ct);
    }

    private async Task ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessFolder");
        activity?.SetTag("folder", folder.FullName);

        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

            var lastUid = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity, ct);

            _logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, lastUid);

            var query = SearchQuery.All;
            if (lastUid > 0)
            {
                var range = new UniqueIdRange(new UniqueId((uint)lastUid + 1), UniqueId.MaxValue);
                query = SearchQuery.Uids(range);
            }
            if (options.StartDate.HasValue) query = query.And(SearchQuery.DeliveredAfter(options.StartDate.Value));
            if (options.EndDate.HasValue) query = query.And(SearchQuery.DeliveredBefore(options.EndDate.Value));

            var uids = await folder.SearchAsync(query, ct);
            _logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            var checkpoint = new FolderCheckpointTracker();

            for (var i = 0; i < uids.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = uids.Skip(i).Take(BatchSize).ToList();
                var failedUids = await DownloadBatchAsync(folder, batch, checkpoint, ct);

                if (checkpoint.SafeCheckpoint > 0)
                {
                    await _storage.UpdateLastUidAsync(folder.FullName, checkpoint.SafeCheckpoint, folder.UidValidity, ct);
                }

                if (failedUids.Count > 0)
                {
                    _logger.LogWarning("Failed to download {Count} emails in {Folder}: UIDs {Uids}",
                        failedUids.Count, folder.FullName, string.Join(", ", failedUids));
                }
            }

            if (checkpoint.LowestFailedUid.HasValue)
            {
                _logger.LogWarning(
                    "Checkpoint for {Folder} held at UID {Checkpoint} so UID {Failed} is retried next run",
                    folder.FullName, checkpoint.SafeCheckpoint, checkpoint.LowestFailedUid.Value);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing folder {Folder}", folder.FullName);
            throw;
        }
    }

    private async Task<List<uint>> DownloadBatchAsync(
        IMailFolder folder,
        IList<UniqueId> uids,
        FolderCheckpointTracker checkpoint,
        CancellationToken ct)
    {
        var failedUids = new List<uint>();

        var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        foreach (var item in items)
        {
            using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessEmail");

            var uid = item.UniqueId.Id;
            var envelope = item.Envelope;
            var envelopeMessageId = envelope?.MessageId;

            if (!string.IsNullOrWhiteSpace(envelopeMessageId))
            {
                var normalizedId = EmailStorageService.NormalizeMessageId(envelopeMessageId);
                if (await _storage.ExistsAsyncNormalized(normalizedId, ct))
                {
                    _logger.LogDebug("Skipping duplicate {Id}", normalizedId);
                    checkpoint.MarkProcessed(uid);
                    continue;
                }
            }

            try
            {
                using var stream = await folder.GetStreamAsync(item.UniqueId, ct);
                var isNew = await _storage.SaveStreamAsync(
                    stream,
                    envelopeMessageId ?? string.Empty,
                    item.InternalDate ?? DateTimeOffset.UtcNow,
                    folder.FullName,
                    ct);

                if (isNew)
                {
                    DiagnosticsConfig.EmailsDownloaded.Add(1);
                    _logger.LogInformation("Downloaded: {Subject}", envelope?.Subject);
                }

                checkpoint.MarkProcessed(uid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
                failedUids.Add(uid);
                checkpoint.MarkFailed(uid);
            }
        }

        return failedUids;
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);
        await client.ConnectAsync(_config.Server, _config.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_config.Username, _config.Password, ct);
    }

    private async Task DisconnectQuietlyAsync(ImapClient client)
    {
        if (!client.IsConnected) return;

        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IMAP disconnect failed");
        }
    }

    private static async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, IMailFolder inbox, CancellationToken ct)
    {
        var folders = new List<IMailFolder>();

        if (client.PersonalNamespaces.Count > 0)
        {
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            await CollectFoldersRecursiveAsync(personal, folders, ct);
        }

        if (!folders.Contains(inbox))
        {
            folders.Insert(0, inbox);
        }

        return folders;
    }

    private static async Task CollectFoldersRecursiveAsync(IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
    {
        foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
        {
            if (IsSelectable(folder))
            {
                folders.Add(folder);
            }

            await CollectFoldersRecursiveAsync(folder, folders, ct);
        }
    }

    private static bool IsSelectable(IMailFolder folder) =>
        (folder.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0;
}
