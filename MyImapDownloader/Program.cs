using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MyImapDownloader;
using MyImapDownloader.Telemetry;

using TelemetryConfiguration = MyImapDownloader.Core.Telemetry.TelemetryConfiguration;

return await DownloadCommand.Parse(DownloadCommand.Create(RunAsync), args).InvokeAsync();

static async Task<int> RunAsync(DownloadOptions options, CancellationToken _)
{
    var host = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddEnvironmentVariables();
        })
        .ConfigureLogging((context, logging) =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
            if (options.Verbose)
            {
                logging.AddFilter(null, LogLevel.Debug);
            }
            logging.AddTelemetryLogging(context.Configuration);
        })
        .ConfigureServices((context, services) =>
        {
            services.AddTelemetry(context.Configuration);
            services.AddSingleton(new ImapConfiguration
            {
                Server = options.Server,
                Username = options.Username,
                Password = options.Password,
                Port = options.Port
            });
            services.AddSingleton(sp => new EmailStorageService(
                sp.GetRequiredService<ILogger<EmailStorageService>>(),
                options.OutputDirectory));
            services.AddTransient<EmailDownloadService>();
        })
        .Build();

    var downloadService = host.Services.GetRequiredService<EmailDownloadService>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var telemetryConfig = host.Services.GetRequiredService<TelemetryConfiguration>();

    using var rootActivity = DiagnosticsConfig.ActivitySource.StartActivity(
        "EmailArchiveSession", ActivityKind.Server);

    rootActivity?.SetTag("service.name", telemetryConfig.ServiceName);
    rootActivity?.SetTag("service.version", telemetryConfig.ServiceVersion);
    rootActivity?.SetTag("host.name", Environment.MachineName);
    rootActivity?.SetTag("process.pid", Environment.ProcessId);
    rootActivity?.SetTag("telemetry.directory", telemetryConfig.OutputDirectory);

    var sessionStopwatch = Stopwatch.StartNew();

    try
    {
        logger.LogInformation("Starting email archive download...");
        logger.LogInformation("Output: {Output}", Path.GetFullPath(options.OutputDirectory));
        logger.LogInformation("Telemetry output: {TelemetryOutput}",
            Path.GetFullPath(telemetryConfig.OutputDirectory));

        rootActivity?.AddEvent(new ActivityEvent("DownloadStarted"));

        await downloadService.DownloadEmailsAsync(options, CancellationToken.None);

        sessionStopwatch.Stop();

        rootActivity?.SetTag("session_duration_ms", sessionStopwatch.ElapsedMilliseconds);
        rootActivity?.SetStatus(ActivityStatusCode.Ok);
        rootActivity?.AddEvent(new ActivityEvent("DownloadCompleted"));

        logger.LogInformation("Archive complete! Session duration: {Duration}ms",
            sessionStopwatch.ElapsedMilliseconds);

        return 0;
    }
    catch (Exception ex)
    {
        rootActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        rootActivity?.RecordException(ex);
        rootActivity?.AddEvent(new ActivityEvent("DownloadFailed", tags: new ActivityTagsCollection
        {
            ["exception.type"] = ex.GetType().FullName,
            ["exception.message"] = ex.Message
        }));

        logger.LogCritical(ex, "Fatal error during download");
        return 1;
    }
    finally
    {
        logger.LogInformation("Flushing telemetry data...");
        await Task.Delay(TimeSpan.FromSeconds(2));
        host.Services.GetService<MyImapDownloader.Core.Telemetry.JsonTelemetryFileWriter>()?.Dispose();
    }
}