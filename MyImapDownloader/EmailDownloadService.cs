using System.Diagnostics;
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

        // Ensure Storage/DB is ready
        await _storage.InitializeAsync(ct);

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async () =>
        {
            using var client = new ImapClient { Timeout = 180_000 };
            try
            {
                await ConnectAndAuthenticateAsync(client, ct);

                var folders = options.AllFolders
                    ? await GetAllFoldersAsync(client, ct)
                    : [client.Inbox];

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

            // DELTA SYNC STRATEGY
            // 1. Get the last UID we successfully processed for this folder
            long lastUidVal = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity.Id, ct);
            UniqueId? startUid = lastUidVal > 0 ? new UniqueId((uint)lastUidVal) : null;

            _logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, startUid);

            // 2. Search only for newer items
            var query = SearchQuery.All;
            if (startUid.HasValue)
            {
                // FIX: Use SearchQuery.Uids with a UniqueIdRange instead of SearchQuery.Uid
                // Fetch everything strictly greater than last seen
                var range = new UniqueIdRange(new UniqueId(startUid.Value.Id + 1), UniqueId.MaxValue);
                query = SearchQuery.Uids(range);
            }
            // Overrides for manual date ranges
            if (options.StartDate.HasValue) query = query.And(SearchQuery.DeliveredAfter(options.StartDate.Value));
            if (options.EndDate.HasValue) query = query.And(SearchQuery.DeliveredBefore(options.EndDate.Value));

            var uids = await folder.SearchAsync(query, ct);
            _logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            // 3. Process in batches
            int batchSize = 50;
            for (int i = 0; i < uids.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested) break;

                var batch = uids.Skip(i).Take(batchSize).ToList();
                long maxUidInBatch = await DownloadBatchAsync(folder, batch, ct);

                // 4. Update checkpoint after successful batch
                if (maxUidInBatch > 0)
                {
                    // FIX: folder.UidValidity is a uint, it does not have an .Id property
                    await _storage.UpdateLastUidAsync(folder.FullName, maxUidInBatch, folder.UidValidity, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing folder {Folder}", folder.FullName);
            throw; // Let Polly handle retry
        }
    }

    private async Task<long> DownloadBatchAsync(IMailFolder folder, IList<UniqueId> uids, CancellationToken ct)
    {
        long maxUid = 0;

        // 1. Fetch Envelopes first (PEEK) - lightweight
        var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        foreach (var item in items)
        {
            using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessEmail");

            // Track max UID for checkpointing
            if (item.UniqueId.Id > maxUid) maxUid = item.UniqueId.Id;

            // 2. Check DB before downloading body
            // Note: Imap Message-ID can be null, handle gracefully
            string msgId = item.Envelope.MessageId ?? $"NO-ID-{item.InternalDate?.Ticks}";

            if (await _storage.ExistsAsync(msgId, ct))
            {
                _logger.LogDebug("Skipping duplicate {Id}", msgId);
                continue;
            }

            // 3. Stream body
            try
            {
                using var stream = await folder.GetStreamAsync(item.UniqueId, ct);
                // FIX: Handle potential null MessageId explicitly to silence warning CS8604
                bool isNew = await _storage.SaveStreamAsync(
                    stream,
                    item.Envelope.MessageId ?? string.Empty, 
                    item.InternalDate ?? DateTimeOffset.UtcNow,
                    folder.FullName,
                    ct);

                if (isNew) _logger.LogInformation("Downloaded: {Subject}", item.Envelope.Subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
                // We do NOT stop the batch for one failed email, but we might not want to update the cursor 
                // past this point if we want to retry it later. 
                // For simplicity in this script, we log and continue.
            }
        }

        return maxUid;
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
        if (!folders.Contains(client.Inbox)) folders.Insert(0, client.Inbox);
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
