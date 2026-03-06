#!/usr/bin/env bash
set -euo pipefail

cd ~/src/dotnet/MyImapDownloader

echo "=== Fixing MyImapDownloader telemetry defects ==="

# ---------------------------------------------------------------------------
# Defect 3: Delete duplicate TelemetryConfiguration.cs
# (identical to MyImapDownloader.Core/Telemetry/TelemetryConfiguration.cs)
# ---------------------------------------------------------------------------
echo "[1/5] Removing duplicate TelemetryConfiguration.cs"
rm -f MyImapDownloader/Telemetry/TelemetryConfiguration.cs

# ---------------------------------------------------------------------------
# Remove redundant test (tests the deleted type; Core already has identical tests)
# ---------------------------------------------------------------------------
echo "[2/5] Removing redundant TelemetryConfigurationTests.cs from MyImapDownloader.Tests"
rm -f MyImapDownloader.Tests/Telemetry/TelemetryConfigurationTests.cs

# ---------------------------------------------------------------------------
# Defect 1: Fix Program.cs — use Core's TelemetryConfiguration via alias
# ---------------------------------------------------------------------------
echo "[3/5] Updating MyImapDownloader/Program.cs"
cat > MyImapDownloader/Program.cs << 'ENDOFFILE'
using System.Diagnostics;

using CommandLine;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MyImapDownloader;
using MyImapDownloader.Telemetry;

using TelemetryConfiguration = MyImapDownloader.Core.Telemetry.TelemetryConfiguration;

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
        var traceWriter = host.Services.GetService<MyImapDownloader.Core.Telemetry.JsonTelemetryFileWriter>();
        traceWriter?.Dispose();
    }
});

parseResult.WithNotParsed(errors =>
{
    Environment.ExitCode = 1;
});
ENDOFFILE

# ---------------------------------------------------------------------------
# Defect 2: Fix local JsonTelemetryFileWriter Dispose ordering
# Must flush BEFORE setting _disposed = true, otherwise FlushAsync
# returns early due to the `if (_disposed) return;` guard.
# ---------------------------------------------------------------------------
echo "[4/5] Fixing MyImapDownloader/Telemetry/JsonTelemetryFileWriter.cs Dispose bug"
cat > MyImapDownloader/Telemetry/JsonTelemetryFileWriter.cs << 'ENDOFFILE'
using System.Collections.Concurrent;
using System.Text.Json;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Thread-safe, async file writer for telemetry data in JSONL format.
/// Each telemetry record is written as a separate JSON line (JSONL format).
/// Gracefully handles write failures without crashing the application.
/// </summary>
public sealed class JsonTelemetryFileWriter : IDisposable
{
    private readonly string _baseDirectory;
    private readonly string _prefix;
    private readonly long _maxFileSizeBytes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<object> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CancellationTokenSource _cts = new();

    private string _currentDate = "";
    private string _currentFilePath = "";
    private int _fileSequence;
    private long _currentFileSize;
    private bool _disposed;
    private bool _writeEnabled = true;

    public JsonTelemetryFileWriter(
        string baseDirectory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _baseDirectory = baseDirectory;
        _prefix = prefix;
        _maxFileSizeBytes = maxFileSizeBytes;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        try
        {
            Directory.CreateDirectory(_baseDirectory);
        }
        catch
        {
            _writeEnabled = false;
        }

        _flushTimer = new Timer(
            _ => FlushTimerCallback(),
            null,
            flushInterval,
            flushInterval);
    }

    private void FlushTimerCallback()
    {
        if (_disposed || !_writeEnabled || _buffer.IsEmpty) return;

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Degrade gracefully - disable writes after buffer grows too large
            if (_buffer.Count > 10000)
            {
                _writeEnabled = false;
                while (_buffer.TryDequeue(out _)) { }
            }
        }
    }

    public void Enqueue(object record)
    {
        if (_disposed || !_writeEnabled) return;
        _buffer.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        // Note: We check _buffer.IsEmpty but NOT _disposed here.
        // This allows the final flush during disposal to complete.
        if (!_writeEnabled || _buffer.IsEmpty) return;

        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;

        try
        {
            var records = new List<object>();
            while (_buffer.TryDequeue(out var record))
            {
                records.Add(record);
            }

            foreach (var record in records)
            {
                await WriteRecordAsync(record);
            }
        }
        catch
        {
            if (_buffer.Count > 10000)
            {
                _writeEnabled = false;
                while (_buffer.TryDequeue(out _)) { }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordAsync(object record)
    {
        if (!_writeEnabled) return;

        try
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            if (today != _currentDate || _currentFileSize >= _maxFileSizeBytes)
            {
                if (today != _currentDate)
                {
                    _currentDate = today;
                    _fileSequence = 0;
                }
                RotateFile();
            }

            string json = JsonSerializer.Serialize(record, record.GetType(), _jsonOptions);
            string line = json + Environment.NewLine;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line);

            if (_currentFileSize + bytes.Length > _maxFileSizeBytes && _currentFileSize > 0)
            {
                RotateFile();
            }

            await File.AppendAllTextAsync(_currentFilePath, line);
            _currentFileSize += bytes.Length;
        }
        catch
        {
            // Individual write failures are silently ignored
        }
    }

    private void RotateFile()
    {
        _fileSequence++;
        _currentFilePath = Path.Combine(
            _baseDirectory,
            $"{_prefix}_{_currentDate}_{_fileSequence:D4}.jsonl");

        try
        {
            _currentFileSize = File.Exists(_currentFilePath) ? new FileInfo(_currentFilePath).Length : 0;
        }
        catch
        {
            _currentFileSize = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Stop the timer first to prevent new timer-driven flushes
        _flushTimer.Dispose();

        // CRITICAL: Flush BEFORE setting _disposed = true.
        // FlushAsync() no longer checks _disposed, so the final flush completes.
        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore flush errors during disposal
        }

        // NOW mark as disposed
        _disposed = true;

        _cts.Cancel();
        _writeLock.Dispose();
        _cts.Dispose();
    }
}
ENDOFFILE

# ---------------------------------------------------------------------------
# Update TelemetryExtensions.cs to use Core's TelemetryConfiguration
# (after deleting the local copy, the Core type resolves automatically
#  via the existing `using MyImapDownloader.Core.Telemetry;` directive,
#  but we add a using alias for clarity)
# ---------------------------------------------------------------------------
echo "[5/5] Updating MyImapDownloader/Telemetry/TelemetryExtensions.cs"
cat > MyImapDownloader/Telemetry/TelemetryExtensions.cs << 'ENDOFFILE'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MyImapDownloader.Core.Telemetry;

using OpenTelemetry;

using TelemetryConfiguration = MyImapDownloader.Core.Telemetry.TelemetryConfiguration;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Extension methods for configuring telemetry in MyImapDownloader.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds telemetry services using the Core infrastructure.
    /// </summary>
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddCoreTelemetry(
            configuration,
            DiagnosticsConfig.ServiceName,
            DiagnosticsConfig.ServiceVersion);
    }

    /// <summary>
    /// Adds telemetry logging.
    /// </summary>
    public static ILoggingBuilder AddTelemetryLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        var config = new TelemetryConfiguration();
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);

        if (!config.EnableLogging)
            return builder;

        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);
        if (telemetryBaseDir == null)
            return builder;

        var logsDir = Path.Combine(telemetryBaseDir, "logs");
        Directory.CreateDirectory(logsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        try
        {
            var logsWriter = new JsonTelemetryFileWriter(
                logsDir, "logs", config.MaxFileSizeBytes, flushInterval);

            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.AddProcessor(new BatchLogRecordExportProcessor(
                    new JsonFileLogExporter(logsWriter),
                    maxQueueSize: 2048,
                    scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds,
                    exporterTimeoutMilliseconds: 30000,
                    maxExportBatchSize: 512));
            });
        }
        catch
        {
            // Continue without log telemetry
        }

        return builder;
    }
}
ENDOFFILE

echo ""
echo "=== Building and testing ==="
dotnet build
dotnet test

echo ""
echo "=== Done ==="
