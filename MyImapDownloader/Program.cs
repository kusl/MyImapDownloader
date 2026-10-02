using System.CommandLine;
using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MyImapDownloader;
using MyImapDownloader.Core.Telemetry;
using MyImapDownloader.Telemetry;

var invocation = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(30) };
return await DownloadCommand.Parse(DownloadCommand.Create(RunAsync), args).InvokeAsync(invocation);

static async Task<int> RunAsync(DownloadOptions options, CancellationToken ct)
{
    var host = BuildHost(options);
    var telemetryWriters = host.Services.GetRequiredService<ITelemetryWriterProvider>();

    try
    {
        host.Services.InitializeCoreTelemetry();
        return await RunSessionAsync(host.Services, options, ct);
    }
    finally
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }

        telemetryWriters.Dispose();
    }
}

static IHost BuildHost(DownloadOptions options) =>
    Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            config.AddEnvironmentVariables();
        })
        .ConfigureLogging((_, logging) =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
            if (options.Verbose)
            {
                logging.AddFilter(null, LogLevel.Debug);
            }
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

static async Task<int> RunSessionAsync(IServiceProvider services, DownloadOptions options, CancellationToken ct)
{
    var downloadService = services.GetRequiredService<EmailDownloadService>();
    var logger = services.GetRequiredService<ILogger<Program>>();
    var telemetryConfig = services.GetRequiredService<TelemetryConfiguration>();

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

        await downloadService.DownloadEmailsAsync(options, ct);

        rootActivity?.SetTag("session_duration_ms", sessionStopwatch.ElapsedMilliseconds);
        rootActivity?.SetStatus(ActivityStatusCode.Ok);
        rootActivity?.AddEvent(new ActivityEvent("DownloadCompleted"));

        logger.LogInformation("Archive complete! Session duration: {Duration}ms",
            sessionStopwatch.ElapsedMilliseconds);

        return 0;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        rootActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
        rootActivity?.AddEvent(new ActivityEvent("DownloadCancelled"));
        logger.LogWarning("Download cancelled after {Duration}ms", sessionStopwatch.ElapsedMilliseconds);
        return 130;
    }
    catch (Exception ex)
    {
        rootActivity.SetErrorStatus(ex);
        rootActivity?.AddEvent(new ActivityEvent("DownloadFailed", tags: new ActivityTagsCollection
        {
            ["exception.type"] = ex.GetType().FullName,
            ["exception.message"] = ex.Message
        }));

        logger.LogCritical(ex, "Fatal error during download");
        return 1;
    }
}
