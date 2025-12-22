using System.Diagnostics;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
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
                retryAttempt => 
                {
                    // Exponential backoff: 2, 4, 8, 16... capped at 5 minutes (300 seconds)
                    var seconds = Math.Min(Math.Pow(2, retryAttempt), 300); 
                    return TimeSpan.FromSeconds(seconds);
                },
                (exception, retryCount, timeSpan, _) =>
                {
                    DiagnosticsConfig.RetryAttempts.Add(1,
                        new KeyValuePair<string, object?>("retry_count", retryCount),
                        new KeyValuePair<string, object?>("exception_type", exception.GetType().Name));

                    _logger.LogWarning(exception,
                        "Connection lost. Retry attempt {RetryCount} in {Delay}. Error: {Message}",
                        retryCount, timeSpan, exception.Message);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not AuthenticationException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(2),
                onBreak: (ex, duration) =>
                {
                    using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
                        "CircuitBreakerOpened", ActivityKind.Internal);
                    activity?.SetTag("duration_seconds", duration.TotalSeconds);
                    activity?.SetTag("exception_type", ex.GetType().Name);
                    activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker opened");

                    _logger.LogError(ex, "Circuit breaker opened for {Duration}", duration);
                },
                onReset: () =>
                {
                    using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
                        "CircuitBreakerReset", ActivityKind.Internal);
                    _logger.LogInformation("Circuit breaker reset");
                });
    }

    public async Task DownloadEmailsAsync(
        DownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "DownloadEmails", ActivityKind.Client);

        activity?.SetTag("server", _config.Server);
        activity?.SetTag("port", _config.Port);
        activity?.SetTag("all_folders", options.AllFolders);
        activity?.SetTag("output_directory", options.OutputDirectory);

        if (options.StartDate.HasValue)
            activity?.SetTag("start_date", options.StartDate.Value.ToString("yyyy-MM-dd"));
        if (options.EndDate.HasValue)
            activity?.SetTag("end_date", options.EndDate.Value.ToString("yyyy-MM-dd"));

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);
        var stats = new DownloadStats();
        var sessionStopwatch = Stopwatch.StartNew();

        try
        {
            await policy.ExecuteAsync(async () =>
            {
                using var client = new ImapClient { Timeout = 180_000 };

                try
                {
                    await ConnectAndAuthenticateAsync(client, cancellationToken);

                    var folders = options.AllFolders
                        ? await GetAllFoldersAsync(client, cancellationToken)
                        : [client.Inbox];

                    activity?.SetTag("folder_count", folders.Count());

                    foreach (var folder in folders)
                    {
                        await DownloadFolderAsync(folder, options, stats, cancellationToken);
                    }
                }
                finally
                {
                    await DisconnectSafelyAsync(client);
                    await _storage.SaveIndexAsync(cancellationToken);
                }
            });

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (AuthenticationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authentication failed");
            activity?.RecordException(ex);
            _logger.LogCritical("Aborting: Authentication failed");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
        finally
        {
            sessionStopwatch.Stop();

            activity?.SetTag("emails_new", stats.NewEmails);
            activity?.SetTag("emails_skipped", stats.SkippedDuplicates);
            activity?.SetTag("emails_errors", stats.Errors);
            activity?.SetTag("total_duration_ms", sessionStopwatch.ElapsedMilliseconds);

            _logger.LogInformation(
                "Download complete. New: {New}, Skipped: {Skipped}, Errors: {Errors}, Duration: {Duration}ms",
                stats.NewEmails, stats.SkippedDuplicates, stats.Errors, sessionStopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<IEnumerable<IMailFolder>> GetAllFoldersAsync(
        ImapClient client, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "GetAllFolders", ActivityKind.Internal);

        var folders = new List<IMailFolder>();
        var personal = client.GetFolder(client.PersonalNamespaces[0]);

        await CollectFoldersRecursiveAsync(personal, folders, ct);

        if (!folders.Contains(client.Inbox))
            folders.Insert(0, client.Inbox);

        activity?.SetTag("folder_count", folders.Count);
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
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "DownloadFolder", ActivityKind.Internal);

        var folderStopwatch = Stopwatch.StartNew();
        activity?.SetTag("folder_name", folder.FullName);

        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

            activity?.SetTag("message_count", folder.Count);
            _logger.LogInformation("Processing folder: {Folder} ({Count} messages)",
                folder.FullName, folder.Count);

            if (folder.Count == 0)
            {
                activity?.AddEvent(new ActivityEvent("EmptyFolder"));
                return;
            }

            var query = BuildSearchQuery(options.StartDate, options.EndDate);
            var uids = query != null
                ? await folder.SearchAsync(query, ct)
                : await folder.SearchAsync(SearchQuery.All, ct);

            activity?.SetTag("matching_messages", uids.Count);
            DiagnosticsConfig.SetQueuedEmails(uids.Count);

            _logger.LogInformation("Found {Count} messages matching criteria", uids.Count);

            const int batchSize = 50;
            for (int i = 0; i < uids.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested) break;

                var batch = uids.Skip(i).Take(batchSize).ToList();
                await DownloadBatchAsync(folder, batch, stats, ct);

                int processed = Math.Min(i + batchSize, uids.Count);
                double progress = (double)processed / uids.Count * 100;

                activity?.AddEvent(new ActivityEvent("BatchComplete", tags: new ActivityTagsCollection
                {
                    ["batch_end"] = processed,
                    ["total"] = uids.Count,
                    ["progress_percent"] = progress
                }));

                _logger.LogInformation("Progress: {Current}/{Total} ({Percent:F1}%)",
                    processed, uids.Count, progress);
            }

            DiagnosticsConfig.FoldersProcessed.Add(1,
                new KeyValuePair<string, object?>("folder_name", folder.FullName));
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                // CHANGE START: Check for connection errors and re-throw
                if (IsTransientConnectionError(ex))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Connection lost, triggering retry");
                    _logger.LogWarning("Connection lost during folder processing: {Message}", ex.Message);
                    throw; // Throwing here allows the outer Polly policy to catch, wait, and reconnect
                }
                // CHANGE END

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.RecordException(ex);
                _logger.LogError(ex, "Error processing folder: {Folder}", folder.FullName);
            }
        finally
        {
            folderStopwatch.Stop();
            DiagnosticsConfig.FolderProcessingDuration.Record(
                folderStopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("folder_name", folder.FullName));
        }
    }

    private async Task DownloadBatchAsync(
        IMailFolder folder,
        IList<UniqueId> uids,
        DownloadStats stats,
        CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "DownloadBatch", ActivityKind.Internal);

        var batchStopwatch = Stopwatch.StartNew();
        activity?.SetTag("folder_name", folder.FullName);
        activity?.SetTag("batch_size", uids.Count);

        int batchNew = 0, batchSkipped = 0, batchErrors = 0;

        foreach (var uid in uids)
        {
            if (ct.IsCancellationRequested) break;

            var emailStopwatch = Stopwatch.StartNew();

            try
            {
                using var emailActivity = DiagnosticsConfig.ActivitySource.StartActivity(
                    "DownloadEmail", ActivityKind.Client);

                emailActivity?.SetTag("folder", folder.FullName);
                emailActivity?.SetTag("uid", uid.Id);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, ct);

                var message = await folder.GetMessageAsync(uid, linkedCts.Token);

                emailActivity?.SetTag("message_id", message.MessageId);
                emailActivity?.SetTag("subject", Truncate(message.Subject, 100));
                emailActivity?.SetTag("from", message.From?.ToString());
                emailActivity?.SetTag("date", message.Date.ToString("O"));

                long messageSize = EstimateMessageSize(message);
                DiagnosticsConfig.EmailSize.Record(messageSize,
                    new KeyValuePair<string, object?>("folder", folder.FullName));

                bool isNew = await _storage.StoreEmailAsync(message, folder.FullName, linkedCts.Token);

                emailStopwatch.Stop();
                DiagnosticsConfig.EmailDownloadDuration.Record(
                    emailStopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("folder", folder.FullName),
                    new KeyValuePair<string, object?>("is_new", isNew));

                if (isNew)
                {
                    batchNew++;
                    stats.NewEmails++;
                    DiagnosticsConfig.EmailsDownloaded.Add(1,
                        new KeyValuePair<string, object?>("folder", folder.FullName));
                    DiagnosticsConfig.BytesDownloaded.Add(messageSize,
                        new KeyValuePair<string, object?>("folder", folder.FullName));
                    DiagnosticsConfig.IncrementTotalEmails();
                }
                else
                {
                    batchSkipped++;
                    stats.SkippedDuplicates++;
                    DiagnosticsConfig.EmailsSkipped.Add(1,
                        new KeyValuePair<string, object?>("folder", folder.FullName));
                }

                emailActivity?.SetTag("is_new", isNew);
                emailActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException)
            {
                batchErrors++;
                stats.Errors++;
                DiagnosticsConfig.EmailErrors.Add(1,
                    new KeyValuePair<string, object?>("folder", folder.FullName),
                    new KeyValuePair<string, object?>("error_type", "timeout"));

                _logger.LogWarning("Timeout downloading message {Uid} in {Folder}", uid, folder.FullName);
            }
            catch (Exception ex)
            {
                // CHANGE START: Check for connection errors and re-throw
        if (IsTransientConnectionError(ex))
        {
             _logger.LogWarning("Connection lost downloading email {Uid}. Bubbling up for reconnect...", uid);
             throw; // Bubble up to folder -> bubble up to policy -> reconnect
        }
        // CHANGE END
        
                batchErrors++;
                stats.Errors++;
                DiagnosticsConfig.EmailErrors.Add(1,
                    new KeyValuePair<string, object?>("folder", folder.FullName),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

                _logger.LogError(ex, "Error downloading {Uid} in {Folder}", uid, folder.FullName);
            }
        }

        batchStopwatch.Stop();
        DiagnosticsConfig.BatchProcessingDuration.Record(
            batchStopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("folder", folder.FullName),
            new KeyValuePair<string, object?>("batch_size", uids.Count));

        activity?.SetTag("new_emails", batchNew);
        activity?.SetTag("skipped_emails", batchSkipped);
        activity?.SetTag("errors", batchErrors);
        activity?.SetTag("duration_ms", batchStopwatch.ElapsedMilliseconds);
    }

    private static long EstimateMessageSize(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.Length;
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
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "ConnectAndAuthenticate", ActivityKind.Client);

        activity?.SetTag("server", _config.Server);
        activity?.SetTag("port", _config.Port);

        DiagnosticsConfig.ConnectionAttempts.Add(1,
            new KeyValuePair<string, object?>("server", _config.Server));

        var connectStopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);

            await client.ConnectAsync(
                _config.Server,
                _config.Port,
                SecureSocketOptions.SslOnConnect,
                ct);

            activity?.AddEvent(new ActivityEvent("Connected"));

            await client.AuthenticateAsync(_config.Username, _config.Password, ct);

            connectStopwatch.Stop();

            DiagnosticsConfig.IncrementActiveConnections();
            activity?.SetTag("connect_duration_ms", connectStopwatch.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation("Connected successfully in {Duration}ms",
                connectStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }

    private async Task DisconnectSafelyAsync(ImapClient client)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "Disconnect", ActivityKind.Client);

        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true);
                DiagnosticsConfig.DecrementActiveConnections();
                activity?.SetStatus(ActivityStatusCode.Ok);
                _logger.LogDebug("Disconnected from server");
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            _logger.LogWarning(ex, "Error during disconnect");
        }
    }

    private static string Truncate(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return "(no subject)";
        return input.Length <= maxLength ? input : input[..(maxLength - 3)] + "...";
    }

    private class DownloadStats
    {
        public int NewEmails;
        public int SkippedDuplicates;
        public int Errors;
    }
    
    private static bool IsTransientConnectionError(Exception ex)
    {
        // Unwrap nested exceptions (like the SocketException inside IOException)
        var baseEx = ex.GetBaseException();

        return ex is IOException 
            || ex is System.Net.Sockets.SocketException 
            || baseEx is System.Net.Sockets.SocketException
            || ex is ImapProtocolException 
            || ex is ServiceNotConnectedException;
    }
}
