using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using MyImapDownloader.Archive;
using MyImapDownloader.Core.Paths;
using MyImapDownloader.Core.Telemetry;
using MyImapDownloader.Imap;
using MyImapDownloader.Sync;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader;

public static class DownloadSession
{
    private const int CancelledExitCode = 130;

    public static async Task<int> RunAsync(DownloadOptions options, CancellationToken ct)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var telemetryOptions = TelemetryOptions.From(
            configuration, DownloaderTelemetry.ServiceName, DownloaderTelemetry.ServiceVersion);

        using var telemetry = JsonFileTelemetry.Start(
            telemetryOptions, DownloaderTelemetry.ServiceName, XdgDirectories.Current);

        using var loggers = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddConsole();
            logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
            if (options.Verbose)
            {
                logging.AddFilter(null, LogLevel.Debug);
            }

            telemetry.ConfigureLogging(logging);
        });

        var logger = loggers.CreateLogger(typeof(DownloadSession));

        using var session = DownloaderTelemetry.ActivitySource.StartActivity("EmailArchiveSession", ActivityKind.Server);
        session?.SetTag("service.name", telemetryOptions.ServiceName);
        session?.SetTag("service.version", telemetryOptions.ServiceVersion);
        session?.SetTag("host.name", Environment.MachineName);
        session?.SetTag("process.pid", Environment.ProcessId);
        session?.SetTag("telemetry.directory", telemetry.OutputDirectory);

        var started = Stopwatch.GetTimestamp();
        long ElapsedMilliseconds() => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        try
        {
            logger.LogInformation("Starting email archive download...");
            logger.LogInformation("Output: {Output}", Path.GetFullPath(options.OutputDirectory));
            logger.LogInformation("Telemetry output: {TelemetryOutput}", telemetry.OutputDirectory ?? "disabled");
            session?.AddEvent(new ActivityEvent("DownloadStarted"));

            await using var archive = await EmailArchive.OpenAsync(
                options.OutputDirectory, loggers, TimeProvider.System, Environment.MachineName, ct);

            var server = new MailKitMailServer(options, loggers.CreateLogger<MailKitMailServer>());
            var folders = new FolderSynchronizer(
                archive, server, loggers.CreateLogger<FolderSynchronizer>(), TimeProvider.System);
            var mailbox = new MailboxSynchronizer(server, folders, loggers.CreateLogger<MailboxSynchronizer>());

            await mailbox.RunAsync(options, ct);

            session?.SetTag("session_duration_ms", ElapsedMilliseconds());
            session?.SetStatus(ActivityStatusCode.Ok);
            session?.AddEvent(new ActivityEvent("DownloadCompleted"));
            logger.LogInformation("Archive complete! Session duration: {Duration}ms", ElapsedMilliseconds());
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            session?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            session?.AddEvent(new ActivityEvent("DownloadCancelled"));
            logger.LogWarning("Download cancelled after {Duration}ms", ElapsedMilliseconds());
            return CancelledExitCode;
        }
        catch (Exception exception)
        {
            session.RecordError(exception);
            session?.AddEvent(new ActivityEvent("DownloadFailed"));
            logger.LogCritical(exception, "Fatal error during download");
            return 1;
        }
    }
}
