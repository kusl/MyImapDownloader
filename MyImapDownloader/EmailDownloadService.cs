using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
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
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning(exception,
                        "Retry {RetryCount} with delay {Delay}",
                        retryCount, timeSpan);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not AuthenticationException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(2));
    }

    public async Task DownloadEmailsAsync(
        DownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);
        var stats = new DownloadStats();

        try
        {
            await policy.ExecuteAsync(async () =>
            {
                using var client = new ImapClient { Timeout = 180_000 };

                try
                {
                    await ConnectAndAuthenticateAsync(client, cancellationToken);
                    
                    // Download all folders, or just INBOX
                    var folders = options.AllFolders 
                        ? await GetAllFoldersAsync(client, cancellationToken)
                        : [client.Inbox];

                    foreach (var folder in folders)
                    {
                        await DownloadFolderAsync(
                            folder, options, stats, cancellationToken);
                    }
                }
                finally
                {
                    await DisconnectSafelyAsync(client);
                    await _storage.SaveIndexAsync(cancellationToken);
                }
            });
        }
        catch (AuthenticationException)
        {
            _logger.LogCritical("Aborting: Authentication failed");
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "Download complete. New: {New}, Skipped: {Skipped}, Errors: {Errors}",
                stats.NewEmails, stats.SkippedDuplicates, stats.Errors);
        }
    }

    private async Task<IEnumerable<IMailFolder>> GetAllFoldersAsync(
        ImapClient client, CancellationToken ct)
    {
        var folders = new List<IMailFolder>();
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        
        await CollectFoldersRecursiveAsync(personal, folders, ct);
        
        // Always include INBOX
        if (!folders.Contains(client.Inbox))
            folders.Insert(0, client.Inbox);
            
        return folders;
    }

    private async Task CollectFoldersRecursiveAsync(
        IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
    {
        foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
        {
            folders.Add(folder);
            await CollectFoldersRecursiveAsync(folder, folders, ct);
        }
    }

    private async Task DownloadFolderAsync(
        IMailFolder folder,
        DownloadOptions options,
        DownloadStats stats,
        CancellationToken ct)
    {
        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            _logger.LogInformation("Processing folder: {Folder} ({Count} messages)",
                folder.FullName, folder.Count);

            if (folder.Count == 0)
                return;

            // Build search query for date filtering
            var query = BuildSearchQuery(options.StartDate, options.EndDate);
            var uids = query != null
                ? await folder.SearchAsync(query, ct)
                : await folder.SearchAsync(SearchQuery.All, ct);

            _logger.LogInformation("Found {Count} messages matching criteria", uids.Count);

            const int batchSize = 50;
            for (int i = 0; i < uids.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested)
                    break;

                var batch = uids.Skip(i).Take(batchSize).ToList();
                await DownloadBatchAsync(folder, batch, stats, ct);
                
                // Progress update
                _logger.LogInformation("Progress: {Current}/{Total} ({Percent:F1}%)",
                    Math.Min(i + batchSize, uids.Count), uids.Count,
                    (double)Math.Min(i + batchSize, uids.Count) / uids.Count * 100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing folder: {Folder}", folder.FullName);
        }
    }

    private async Task DownloadBatchAsync(
        IMailFolder folder,
        IList<UniqueId> uids,
        DownloadStats stats,
        CancellationToken ct)
    {
        foreach (var uid in uids)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, ct);

                var message = await folder.GetMessageAsync(uid, linkedCts.Token);
                bool isNew = await _storage.StoreEmailAsync(
                    message, folder.FullName, linkedCts.Token);

                if (isNew)
                    stats.NewEmails++;
                else
                    stats.SkippedDuplicates++;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout downloading message {Uid} in {Folder}",
                    uid, folder.FullName);
                stats.Errors++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading {Uid} in {Folder}",
                    uid, folder.FullName);
                stats.Errors++;
            }
        }
    }

    private static SearchQuery? BuildSearchQuery(DateTime? startDate, DateTime? endDate)
    {
        SearchQuery? query = null;

        if (startDate.HasValue)
            query = SearchQuery.DeliveredAfter(startDate.Value);

        if (endDate.HasValue)
        {
            var endQuery = SearchQuery.DeliveredBefore(endDate.Value.AddDays(1));
            query = query != null ? query.And(endQuery) : endQuery;
        }

        return query;
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);
        
        await client.ConnectAsync(
            _config.Server,
            _config.Port,
            SecureSocketOptions.SslOnConnect,
            ct);

        await client.AuthenticateAsync(_config.Username, _config.Password, ct);
        _logger.LogInformation("Connected successfully");
    }

    private async Task DisconnectSafelyAsync(ImapClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true);
                _logger.LogDebug("Disconnected from server");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect");
        }
    }

    private class DownloadStats
    {
        public int NewEmails;
        public int SkippedDuplicates;
        public int Errors;
    }
}
