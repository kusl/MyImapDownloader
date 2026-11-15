using Microsoft.Extensions.Logging;

using MyImapDownloader.Imap;
using MyImapDownloader.Telemetry;

using Polly;
using Polly.Retry;

namespace MyImapDownloader.Sync;

public sealed class MailboxSynchronizer
{
    private const double MaxBackoffSeconds = 300;

    private readonly IMailServer _server;
    private readonly FolderSynchronizer _folders;
    private readonly ILogger<MailboxSynchronizer> _logger;
    private readonly ResiliencePipeline _pipeline;

    public MailboxSynchronizer(
        IMailServer server,
        FolderSynchronizer folders,
        ILogger<MailboxSynchronizer> logger,
        Func<int, TimeSpan>? backoff = null)
    {
        _server = server;
        _folders = folders;
        _logger = logger;

        var delay = backoff ?? Backoff;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = arguments => ValueTask.FromResult(
                    arguments.Outcome.Exception is { } exception && IsRetryable(exception)),
                DelayGenerator = arguments => ValueTask.FromResult<TimeSpan?>(delay(arguments.AttemptNumber)),
                OnRetry = arguments =>
                {
                    DownloaderTelemetry.RetryAttempts.Add(1);
                    _logger.LogWarning(
                        arguments.Outcome.Exception,
                        "Retry {Count} in {Delay}: {Message}",
                        arguments.AttemptNumber + 1,
                        arguments.RetryDelay,
                        arguments.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt + 1), MaxBackoffSeconds));

    public async Task RunAsync(DownloadOptions options, CancellationToken ct)
    {
        using var activity = DownloaderTelemetry.ActivitySource.StartActivity("DownloadEmails");

        await _pipeline.ExecuteAsync(
            async token =>
            {
                await using var session = await _server.ConnectAsync(token);
                foreach (var folder in await session.GetFoldersAsync(options.AllFolders, token))
                {
                    await _folders.SyncAsync(folder, options, token);
                }
            },
            ct);
    }

    private bool IsRetryable(Exception exception) =>
        exception is not OperationCanceledException && _server.Classify(exception) != MailFailure.Authentication;
}
