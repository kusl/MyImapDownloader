using System.Diagnostics;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyImapDownloader;
using MyImapDownloader.Telemetry;

var parseResult = Parser.Default.ParseArguments<DownloadOptions>(args);

await parseResult.WithParsedAsync(async options =>
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
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
            logging.AddTelemetryLogging(context.Configuration);
        })
        .ConfigureServices((context, services) =>
        {
            // Add telemetry
            services.AddTelemetry(context.Configuration);

            services.AddSingleton(options);
            services.AddSingleton(new ImapConfiguration
            {
                Server = options.Server,
                Username = options.Username,
                Password = options.Password,
                Port = options.Port
            });
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EmailStorageService>>();
                return new EmailStorageService(logger, options.OutputDirectory);
            });
            services.AddTransient<EmailDownloadService>();
        })
        .Build();

    var downloadService = host.Services.GetRequiredService<EmailDownloadService>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var telemetryConfig = host.Services.GetRequiredService<TelemetryConfiguration>();

    // Create root activity for the entire session
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
        Environment.ExitCode = 1;
    }
    finally
    {
        // Ensure all telemetry is flushed before exit
        logger.LogInformation("Flushing telemetry data...");

        // Give time for async exporters to flush
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Dispose file writers to flush remaining data
        var traceWriter = host.Services.GetService<JsonTelemetryFileWriter>();
        traceWriter?.Dispose();
    }
});

parseResult.WithNotParsed(errors =>
{
    Environment.ExitCode = 1;
});
