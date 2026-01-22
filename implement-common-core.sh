#!/bin/bash
# =============================================================================
# MyImapDownloader Common Core Integration Script
# =============================================================================
# This script creates a shared library (MyImapDownloader.Core) containing
# common infrastructure used by both MyImapDownloader and MyEmailSearch.
#
# Components moved to Core:
#   - Telemetry (DiagnosticsConfig, exporters, file writer)
#   - Path resolution (XDG-compliant directory resolution)
#   - Common models (EmailMetadata)
#   - Database helpers (SQLite base patterns)
#   - Test infrastructure (TempDirectory, TestLogger)
# =============================================================================

set -euo pipefail

PROJECT_ROOT="${1:-$(pwd)}"
CORE_DIR="$PROJECT_ROOT/MyImapDownloader.Core"
CORE_TESTS_DIR="$PROJECT_ROOT/MyImapDownloader.Core.Tests"

echo "=========================================="
echo "Creating MyImapDownloader.Core Library"
echo "Project Root: $PROJECT_ROOT"
echo "=========================================="

# =============================================================================
# 1. Create Core project structure
# =============================================================================
echo ""
echo "[1/8] Creating directory structure..."

mkdir -p "$CORE_DIR/Telemetry"
mkdir -p "$CORE_DIR/Configuration"
mkdir -p "$CORE_DIR/Data"
mkdir -p "$CORE_DIR/Infrastructure"
mkdir -p "$CORE_TESTS_DIR/Telemetry"
mkdir -p "$CORE_TESTS_DIR/Configuration"
mkdir -p "$CORE_TESTS_DIR/Infrastructure"
mkdir -p "$CORE_TESTS_DIR/TestFixtures"

# =============================================================================
# 2. Create Core project file
# =============================================================================
echo "[2/8] Creating MyImapDownloader.Core.csproj..."

cat > "$CORE_DIR/MyImapDownloader.Core.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    MyImapDownloader.Core - Shared Infrastructure Library
    
    This library contains common components shared between:
      - MyImapDownloader (email archiving)
      - MyEmailSearch (email search)
    
    Components:
      - Telemetry: OpenTelemetry with JSONL file exporters
      - Configuration: XDG-compliant path resolution
      - Data: Common email metadata models
      - Infrastructure: SQLite helpers, test utilities
  -->
  
  <PropertyGroup>
    <RootNamespace>MyImapDownloader.Core</RootNamespace>
    <AssemblyName>MyImapDownloader.Core</AssemblyName>
    <Description>Shared infrastructure for MyImapDownloader and MyEmailSearch</Description>
  </PropertyGroup>

  <ItemGroup>
    <!-- Database -->
    <PackageReference Include="Microsoft.Data.Sqlite" />
    
    <!-- Telemetry -->
    <PackageReference Include="OpenTelemetry" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    
    <!-- Configuration & DI -->
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
  </ItemGroup>
</Project>
CSPROJ

# =============================================================================
# 3. Create Telemetry components
# =============================================================================
echo "[3/8] Creating Telemetry components..."

# 3.1 DiagnosticsConfig.cs - Base telemetry configuration
cat > "$CORE_DIR/Telemetry/DiagnosticsConfig.cs" << 'CSHARP'
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Base diagnostics configuration for shared telemetry infrastructure.
/// Applications should create their own derived config with application-specific metrics.
/// </summary>
public class DiagnosticsConfigBase
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    public DiagnosticsConfigBase(string serviceName, string serviceVersion)
    {
        ServiceName = serviceName;
        ServiceVersion = serviceVersion;
        _activitySource = new ActivitySource(serviceName, serviceVersion);
        _meter = new Meter(serviceName, serviceVersion);
    }

    public string ServiceName { get; }
    public string ServiceVersion { get; }
    public ActivitySource ActivitySource => _activitySource;
    public Meter Meter => _meter;

    /// <summary>
    /// Creates a counter metric.
    /// </summary>
    public Counter<T> CreateCounter<T>(string name, string? unit = null, string? description = null)
        where T : struct
        => _meter.CreateCounter<T>(name, unit, description);

    /// <summary>
    /// Creates a histogram metric.
    /// </summary>
    public Histogram<T> CreateHistogram<T>(string name, string? unit = null, string? description = null)
        where T : struct
        => _meter.CreateHistogram<T>(name, unit, description);

    /// <summary>
    /// Creates an observable gauge metric.
    /// </summary>
    public ObservableGauge<T> CreateObservableGauge<T>(
        string name,
        Func<T> observeValue,
        string? unit = null,
        string? description = null)
        where T : struct
        => _meter.CreateObservableGauge(name, observeValue, unit, description);
}

/// <summary>
/// Static helper for creating activity spans.
/// </summary>
public static class ActivityHelper
{
    /// <summary>
    /// Starts an activity with the given source and name.
    /// </summary>
    public static Activity? StartActivity(
        ActivitySource source,
        string name,
        ActivityKind kind = ActivityKind.Internal)
        => source.StartActivity(name, kind);
}
CSHARP

# 3.2 TelemetryConfiguration.cs
cat > "$CORE_DIR/Telemetry/TelemetryConfiguration.cs" << 'CSHARP'
namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Configuration options for telemetry export.
/// </summary>
public class TelemetryConfiguration
{
    public const string SectionName = "Telemetry";

    public string ServiceName { get; set; } = "MyImapDownloader";
    public string ServiceVersion { get; set; } = "1.0.0";
    public string OutputDirectory { get; set; } = "telemetry";
    public int MaxFileSizeMB { get; set; } = 25;
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public int FlushIntervalSeconds { get; set; } = 5;
    public int MetricsExportIntervalSeconds { get; set; } = 15;

    public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;
}
CSHARP

# 3.3 JsonTelemetryFileWriter.cs
cat > "$CORE_DIR/Telemetry/JsonTelemetryFileWriter.cs" << 'CSHARP'
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Thread-safe JSON Lines file writer with size-based rotation and periodic flushing.
/// </summary>
public sealed class JsonTelemetryFileWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSize;
    private readonly TimeSpan _flushInterval;
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly CancellationTokenSource _cts = new();

    private string _currentFilePath;
    private long _currentFileSize;
    private int _fileSequence;
    private bool _disposed;

    public JsonTelemetryFileWriter(
        string directory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _directory = directory;
        _prefix = prefix;
        _maxFileSize = maxFileSizeBytes;
        _flushInterval = flushInterval;

        Directory.CreateDirectory(directory);
        _currentFilePath = GenerateFilePath();
        InitializeFileSize();

        _flushTimer = new Timer(
            _ => _ = FlushAsync(),
            null,
            flushInterval,
            flushInterval);
    }

    public void Enqueue(object record)
    {
        if (_disposed) return;
        _queue.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        if (_disposed || _queue.IsEmpty) return;

        var records = new List<object>();
        while (_queue.TryDequeue(out var record))
        {
            records.Add(record);
        }

        if (records.Count == 0) return;

        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            await WriteRecordsAsync(records);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordsAsync(List<object> records)
    {
        var sb = new StringBuilder();
        foreach (var record in records)
        {
            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            sb.AppendLine(json);
        }

        var content = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(content);

        if (_currentFileSize + bytes.Length > _maxFileSize)
        {
            RotateFile();
        }

        await File.AppendAllTextAsync(_currentFilePath, content, _cts.Token);
        _currentFileSize += bytes.Length;
    }

    private void RotateFile()
    {
        _fileSequence++;
        _currentFilePath = GenerateFilePath();
        _currentFileSize = 0;
    }

    private string GenerateFilePath()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        return Path.Combine(_directory, $"{_prefix}_{date}_{_fileSequence:D4}.jsonl");
    }

    private void InitializeFileSize()
    {
        try
        {
            _currentFileSize = File.Exists(_currentFilePath)
                ? new FileInfo(_currentFilePath).Length
                : 0;
        }
        catch
        {
            _currentFileSize = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _flushTimer.Dispose();

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore flush errors during disposal
        }

        _writeLock.Dispose();
        _cts.Dispose();
    }
}
CSHARP

# 3.4 ActivityExtensions.cs
cat > "$CORE_DIR/Telemetry/ActivityExtensions.cs" << 'CSHARP'
using System.Diagnostics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Extension methods for System.Diagnostics.Activity.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Records an exception on the activity with full details.
    /// </summary>
    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null) return;

        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message }
        };

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            tags.Add("exception.stacktrace", exception.StackTrace);
        }

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    /// <summary>
    /// Sets the activity status to OK.
    /// </summary>
    public static void SetSuccess(this Activity? activity, string? description = null)
    {
        activity?.SetStatus(ActivityStatusCode.Ok, description);
    }

    /// <summary>
    /// Sets the activity status to Error.
    /// </summary>
    public static void SetError(this Activity? activity, string? description = null)
    {
        activity?.SetStatus(ActivityStatusCode.Error, description);
    }

    /// <summary>
    /// Adds a tag if the value is not null or empty.
    /// </summary>
    public static Activity? SetTagIfNotEmpty(this Activity? activity, string key, string? value)
    {
        if (activity != null && !string.IsNullOrEmpty(value))
        {
            activity.SetTag(key, value);
        }
        return activity;
    }
}
CSHARP

# 3.5 JsonFileLogExporter.cs
cat > "$CORE_DIR/Telemetry/JsonFileLogExporter.cs" << 'CSHARP'
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Exports OpenTelemetry logs to JSON files.
/// </summary>
public sealed class JsonFileLogExporter(JsonTelemetryFileWriter? writer) : BaseExporter<LogRecord>
{
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var log in batch)
            {
                var record = new LogRecordData
                {
                    Timestamp = log.Timestamp != default ? log.Timestamp : DateTime.UtcNow,
                    TraceId = log.TraceId != default ? log.TraceId.ToString() : null,
                    SpanId = log.SpanId != default ? log.SpanId.ToString() : null,
                    LogLevel = log.LogLevel.ToString(),
                    CategoryName = log.CategoryName,
                    EventId = log.EventId.Id != 0 ? log.EventId.Id : null,
                    EventName = log.EventId.Name,
                    FormattedMessage = log.FormattedMessage,
                    Body = log.Body,
                    Attributes = ExtractAttributes(log),
                    Exception = ExtractException(log.Exception)
                };

                writer.Enqueue(record);
            }
        }
        catch
        {
            // Silently ignore export failures
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, object?>? ExtractAttributes(LogRecord log)
    {
        if (log.Attributes == null) return null;

        var attrs = new Dictionary<string, object?>();
        foreach (var attr in log.Attributes)
        {
            attrs[attr.Key] = attr.Value;
        }
        return attrs.Count > 0 ? attrs : null;
    }

    private static ExceptionInfo? ExtractException(Exception? ex)
    {
        if (ex == null) return null;

        return new ExceptionInfo
        {
            Type = ex.GetType().FullName,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = ExtractException(ex.InnerException)
        };
    }
}

public record LogRecordData
{
    public string Type => "log";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? LogLevel { get; init; }
    public string? CategoryName { get; init; }
    public int? EventId { get; init; }
    public string? EventName { get; init; }
    public string? FormattedMessage { get; init; }
    public string? Body { get; init; }
    public Dictionary<string, object?>? Attributes { get; init; }
    public ExceptionInfo? Exception { get; init; }
}

public record ExceptionInfo
{
    public string? Type { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public ExceptionInfo? InnerException { get; init; }
}
CSHARP

# 3.6 JsonFileTraceExporter.cs
cat > "$CORE_DIR/Telemetry/JsonFileTraceExporter.cs" << 'CSHARP'
using System.Diagnostics;
using OpenTelemetry;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Exports OpenTelemetry traces to JSON files.
/// </summary>
public sealed class JsonFileTraceExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var activity in batch)
            {
                var record = new TraceRecord
                {
                    Timestamp = activity.StartTimeUtc,
                    TraceId = activity.TraceId.ToString(),
                    SpanId = activity.SpanId.ToString(),
                    ParentSpanId = activity.ParentSpanId.ToString(),
                    OperationName = activity.OperationName,
                    DisplayName = activity.DisplayName,
                    Kind = activity.Kind.ToString(),
                    Status = activity.Status.ToString(),
                    StatusDescription = activity.StatusDescription,
                    Duration = activity.Duration,
                    DurationMs = activity.Duration.TotalMilliseconds,
                    Source = new SourceInfo
                    {
                        Name = activity.Source.Name,
                        Version = activity.Source.Version
                    },
                    Tags = activity.Tags.ToDictionary(t => t.Key, t => t.Value),
                    Events = activity.Events.Select(e => new SpanEvent
                    {
                        Name = e.Name,
                        Timestamp = e.Timestamp.UtcDateTime,
                        Attributes = e.Tags.ToDictionary(t => t.Key, t => t.Value?.ToString())
                    }).ToList()
                };

                writer.Enqueue(record);
            }
        }
        catch
        {
            // Silently ignore export failures
        }

        return ExportResult.Success;
    }
}

public record TraceRecord
{
    public string Type => "trace";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? OperationName { get; init; }
    public string? DisplayName { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    public TimeSpan Duration { get; init; }
    public double DurationMs { get; init; }
    public SourceInfo? Source { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public List<SpanEvent>? Events { get; init; }
}

public record SourceInfo
{
    public string? Name { get; init; }
    public string? Version { get; init; }
}

public record SpanEvent
{
    public string? Name { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
}
CSHARP

# 3.7 JsonFileMetricsExporter.cs
cat > "$CORE_DIR/Telemetry/JsonFileMetricsExporter.cs" << 'CSHARP'
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Exports OpenTelemetry metrics to JSON files.
/// </summary>
public sealed class JsonFileMetricsExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var record = new MetricRecord
                    {
                        Timestamp = point.EndTime.UtcDateTime,
                        MetricName = metric.Name,
                        MetricDescription = metric.Description,
                        MetricUnit = metric.Unit,
                        MetricType = metric.MetricType.ToString(),
                        MeterName = metric.MeterName,
                        StartTime = point.StartTime.UtcDateTime,
                        EndTime = point.EndTime.UtcDateTime,
                        Tags = ExtractTags(point),
                        Value = ExtractValue(metric, point)
                    };

                    writer.Enqueue(record);
                }
            }
        }
        catch
        {
            // Silently ignore export failures
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?>? ExtractTags(MetricPoint point)
    {
        var tags = new Dictionary<string, string?>();
        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }
        return tags.Count > 0 ? tags : null;
    }

    private static object? ExtractValue(Metric metric, MetricPoint point)
    {
        return metric.MetricType switch
        {
            MetricType.LongSum => point.GetSumLong(),
            MetricType.DoubleSum => point.GetSumDouble(),
            MetricType.LongGauge => point.GetGaugeLastValueLong(),
            MetricType.DoubleGauge => point.GetGaugeLastValueDouble(),
            MetricType.Histogram => new
            {
                Count = point.GetHistogramCount(),
                Sum = point.GetHistogramSum(),
                Min = GetHistogramMin(point),
                Max = GetHistogramMax(point)
            },
            _ => null
        };
    }

    private static double? GetHistogramMin(MetricPoint point)
    {
        try
        {
            var prop = point.GetType().GetProperty("HistogramMin");
            return prop?.GetValue(point) as double?;
        }
        catch { return null; }
    }

    private static double? GetHistogramMax(MetricPoint point)
    {
        try
        {
            var prop = point.GetType().GetProperty("HistogramMax");
            return prop?.GetValue(point) as double?;
        }
        catch { return null; }
    }
}

public record MetricRecord
{
    public string Type => "metric";
    public DateTime Timestamp { get; init; }
    public string? MetricName { get; init; }
    public string? MetricDescription { get; init; }
    public string? MetricUnit { get; init; }
    public string? MetricType { get; init; }
    public string? MeterName { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public object? Value { get; init; }
}
CSHARP

# 3.8 TelemetryDirectoryResolver.cs
cat > "$CORE_DIR/Telemetry/TelemetryDirectoryResolver.cs" << 'CSHARP'
namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Resolves telemetry output directory following XDG Base Directory Specification.
/// </summary>
public static class TelemetryDirectoryResolver
{
    /// <summary>
    /// Attempts to resolve a writable telemetry directory.
    /// Returns null if no writable location can be found.
    /// </summary>
    public static string? ResolveTelemetryDirectory(string appName)
    {
        var candidates = GetCandidateDirectories(appName);

        foreach (var candidate in candidates)
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories(string appName)
    {
        var lowerAppName = appName.ToLowerInvariant();

        // 1. XDG_STATE_HOME (preferred for telemetry/logs)
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdgState))
        {
            yield return Path.Combine(xdgState, lowerAppName, "telemetry");
        }

        // 2. XDG_DATA_HOME
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgData))
        {
            yield return Path.Combine(xdgData, lowerAppName, "telemetry");
        }

        // 3. Platform-specific defaults
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, ".local", "state", lowerAppName, "telemetry");
            yield return Path.Combine(home, ".local", "share", lowerAppName, "telemetry");
        }
        else if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, appName, "telemetry");
        }

        // 4. Fallback to current directory
        yield return Path.Combine(Environment.CurrentDirectory, "telemetry");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
CSHARP

# 3.9 TelemetryExtensions.cs
cat > "$CORE_DIR/Telemetry/TelemetryExtensions.cs" << 'CSHARP'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry with JSON file exporters.
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddCoreTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion)
    {
        var config = new TelemetryConfiguration
        {
            ServiceName = serviceName,
            ServiceVersion = serviceVersion
        };
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);
        services.AddSingleton(config);

        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);

        if (telemetryBaseDir == null)
        {
            services.AddSingleton<ITelemetryWriterProvider>(new NullTelemetryWriterProvider());
            return services;
        }

        config.OutputDirectory = telemetryBaseDir;

        var tracesDir = Path.Combine(telemetryBaseDir, "traces");
        var metricsDir = Path.Combine(telemetryBaseDir, "metrics");

        TryCreateDirectory(tracesDir);
        TryCreateDirectory(metricsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        JsonTelemetryFileWriter? traceWriter = null;
        JsonTelemetryFileWriter? metricsWriter = null;

        if (config.EnableTracing)
        {
            try
            {
                traceWriter = new JsonTelemetryFileWriter(
                    tracesDir, "traces", config.MaxFileSizeBytes, flushInterval);
            }
            catch { }
        }

        if (config.EnableMetrics)
        {
            try
            {
                metricsWriter = new JsonTelemetryFileWriter(
                    metricsDir, "metrics", config.MaxFileSizeBytes, flushInterval);
            }
            catch { }
        }

        services.AddSingleton<ITelemetryWriterProvider>(
            new TelemetryWriterProvider(traceWriter, metricsWriter, null));

        if (traceWriter != null)
        {
            services.AddSingleton(traceWriter);
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: config.ServiceName, serviceVersion: config.ServiceVersion);

        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                if (config.EnableTracing && traceWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(config.ServiceName)
                        .AddProcessor(new BatchActivityExportProcessor(
                            new JsonFileTraceExporter(traceWriter),
                            maxQueueSize: 2048,
                            scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds));
                }
            })
            .WithMetrics(builder =>
            {
                if (config.EnableMetrics && metricsWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(config.ServiceName)
                        .AddRuntimeInstrumentation()
                        .AddReader(new PeriodicExportingMetricReader(
                            new JsonFileMetricsExporter(metricsWriter),
                            exportIntervalMilliseconds: config.MetricsExportIntervalSeconds * 1000));
                }
            });

        return services;
    }

    private static bool TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Interface for accessing telemetry file writers.
/// </summary>
public interface ITelemetryWriterProvider
{
    JsonTelemetryFileWriter? TraceWriter { get; }
    JsonTelemetryFileWriter? MetricsWriter { get; }
    JsonTelemetryFileWriter? LogsWriter { get; }
}

/// <summary>
/// Provides access to telemetry file writers.
/// </summary>
public sealed class TelemetryWriterProvider(
    JsonTelemetryFileWriter? traceWriter,
    JsonTelemetryFileWriter? metricsWriter,
    JsonTelemetryFileWriter? logsWriter) : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => traceWriter;
    public JsonTelemetryFileWriter? MetricsWriter => metricsWriter;
    public JsonTelemetryFileWriter? LogsWriter => logsWriter;
}

/// <summary>
/// Null implementation when telemetry is disabled.
/// </summary>
public sealed class NullTelemetryWriterProvider : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => null;
    public JsonTelemetryFileWriter? MetricsWriter => null;
    public JsonTelemetryFileWriter? LogsWriter => null;
}
CSHARP

# =============================================================================
# 4. Create Configuration components
# =============================================================================
echo "[4/8] Creating Configuration components..."

# 4.1 PathResolver.cs - Unified XDG path resolution
cat > "$CORE_DIR/Configuration/PathResolver.cs" << 'CSHARP'
namespace MyImapDownloader.Core.Configuration;

/// <summary>
/// Resolves paths following XDG Base Directory Specification.
/// Provides consistent cross-platform path resolution for all applications.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Gets the XDG data home directory.
    /// </summary>
    public static string GetDataHome(string appName)
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        }

        return Path.Combine(home, ".local", "share", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Gets the XDG config home directory.
    /// </summary>
    public static string GetConfigHome(string appName)
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName);
        }

        return Path.Combine(home, ".config", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Gets the XDG state home directory (for logs, telemetry, etc.).
    /// </summary>
    public static string GetStateHome(string appName)
    {
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome))
        {
            return Path.Combine(xdgStateHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName,
                "State");
        }

        return Path.Combine(home, ".local", "state", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Finds the first existing path from a list of candidates.
    /// </summary>
    public static string? FindFirstExisting(params string[] candidates)
    {
        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Ensures a directory exists and is writable.
    /// </summary>
    public static bool EnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
CSHARP

# =============================================================================
# 5. Create Data models
# =============================================================================
echo "[5/8] Creating Data models..."

# 5.1 EmailMetadata.cs - Shared email metadata model
cat > "$CORE_DIR/Data/EmailMetadata.cs" << 'CSHARP'
namespace MyImapDownloader.Core.Data;

/// <summary>
/// Represents metadata for an archived email.
/// This is the common model used by both the downloader and search systems.
/// </summary>
public record EmailMetadata
{
    /// <summary>
    /// The unique message ID from the email headers.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// The email subject line.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The sender address (From header).
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// The recipient addresses (To header).
    /// </summary>
    public string? To { get; init; }

    /// <summary>
    /// The CC addresses.
    /// </summary>
    public string? Cc { get; init; }

    /// <summary>
    /// The date the email was sent.
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// The folder/mailbox where the email is stored.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// When this email was archived.
    /// </summary>
    public DateTimeOffset ArchivedAt { get; init; }

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// The account this email belongs to.
    /// </summary>
    public string? Account { get; init; }
}
CSHARP

# =============================================================================
# 6. Create Infrastructure components
# =============================================================================
echo "[6/8] Creating Infrastructure components..."

# 6.1 SqliteHelper.cs - Common SQLite patterns
cat > "$CORE_DIR/Infrastructure/SqliteHelper.cs" << 'CSHARP'
using Microsoft.Data.Sqlite;

namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Helper class for common SQLite operations.
/// </summary>
public static class SqliteHelper
{
    /// <summary>
    /// Creates a connection string with recommended settings.
    /// </summary>
    public static string CreateConnectionString(string dbPath, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Applies recommended pragmas for performance and safety.
    /// </summary>
    public static async Task ApplyRecommendedPragmasAsync(
        SqliteConnection connection,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 268435456;
            PRAGMA cache_size = -64000;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a non-query command.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            }
        }

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a scalar query.
    /// </summary>
    public static async Task<T?> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            }
        }

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value)
        {
            return default;
        }
        return (T)Convert.ChangeType(result, typeof(T));
    }
}
CSHARP

# 6.2 TempDirectory.cs - Test helper
cat > "$CORE_DIR/Infrastructure/TempDirectory.cs" << 'CSHARP'
namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Creates a temporary directory that is automatically cleaned up on disposal.
/// Useful for tests and temporary file operations.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string? prefix = null)
    {
        var name = prefix ?? "temp";
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
CSHARP

# 6.3 TestLogger.cs - Test logging helper
cat > "$CORE_DIR/Infrastructure/TestLogger.cs" << 'CSHARP'
using Microsoft.Extensions.Logging;

namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Factory for creating test loggers.
/// </summary>
public static class TestLogger
{
    /// <summary>
    /// Creates a logger that writes to the console.
    /// </summary>
    public static ILogger<T> Create<T>()
    {
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        return factory.CreateLogger<T>();
    }

    /// <summary>
    /// Creates a null logger that discards all output.
    /// </summary>
    public static ILogger<T> CreateNull<T>()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
    }
}
CSHARP

# =============================================================================
# 7. Create Core Tests project
# =============================================================================
echo "[7/8] Creating Core Tests project..."

cat > "$CORE_TESTS_DIR/MyImapDownloader.Core.Tests.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>
</Project>
CSPROJ

# Test: TelemetryConfigurationTests.cs
cat > "$CORE_TESTS_DIR/Telemetry/TelemetryConfigurationTests.cs" << 'CSHARP'
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class TelemetryConfigurationTests
{
    [Test]
    public async Task DefaultValues_AreReasonable()
    {
        var config = new TelemetryConfiguration();

        await Assert.That(config.ServiceName).IsEqualTo("MyImapDownloader");
        await Assert.That(config.ServiceVersion).IsEqualTo("1.0.0");
        await Assert.That(config.EnableTracing).IsTrue();
        await Assert.That(config.EnableMetrics).IsTrue();
        await Assert.That(config.EnableLogging).IsTrue();
        await Assert.That(config.MaxFileSizeMB).IsEqualTo(25);
        await Assert.That(config.FlushIntervalSeconds).IsEqualTo(5);
    }

    [Test]
    public async Task MaxFileSizeBytes_CalculatesCorrectly()
    {
        var config = new TelemetryConfiguration { MaxFileSizeMB = 10 };
        await Assert.That(config.MaxFileSizeBytes).IsEqualTo(10L * 1024L * 1024L);
    }

    [Test]
    [Arguments(1)]
    [Arguments(25)]
    [Arguments(100)]
    public async Task MaxFileSizeBytes_ScalesWithMB(int megabytes)
    {
        var config = new TelemetryConfiguration { MaxFileSizeMB = megabytes };
        var expected = (long)megabytes * 1024L * 1024L;
        await Assert.That(config.MaxFileSizeBytes).IsEqualTo(expected);
    }
}
CSHARP

# Test: PathResolverTests.cs
cat > "$CORE_TESTS_DIR/Configuration/PathResolverTests.cs" << 'CSHARP'
using MyImapDownloader.Core.Configuration;

namespace MyImapDownloader.Core.Tests.Configuration;

public class PathResolverTests
{
    [Test]
    public async Task GetDataHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDataHome("TestApp");
        
        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetConfigHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetConfigHome("TestApp");
        
        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetStateHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetStateHome("TestApp");
        
        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task EnsureWritableDirectory_CreatesDirectory()
    {
        using var temp = new MyImapDownloader.Core.Infrastructure.TempDirectory("path_test");
        var subDir = Path.Combine(temp.Path, "subdir");

        var result = PathResolver.EnsureWritableDirectory(subDir);

        await Assert.That(result).IsTrue();
        await Assert.That(Directory.Exists(subDir)).IsTrue();
    }

    [Test]
    public async Task FindFirstExisting_ReturnsFirstMatch()
    {
        using var temp = new MyImapDownloader.Core.Infrastructure.TempDirectory("find_test");
        
        var result = PathResolver.FindFirstExisting(
            "/nonexistent/path",
            temp.Path,
            "/another/nonexistent");

        await Assert.That(result).IsEqualTo(temp.Path);
    }
}
CSHARP

# Test: TempDirectoryTests.cs
cat > "$CORE_TESTS_DIR/Infrastructure/TempDirectoryTests.cs" << 'CSHARP'
using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Core.Tests.Infrastructure;

public class TempDirectoryTests
{
    [Test]
    public async Task Constructor_CreatesDirectory()
    {
        using var temp = new TempDirectory("test");
        await Assert.That(Directory.Exists(temp.Path)).IsTrue();
    }

    [Test]
    public async Task Dispose_DeletesDirectory()
    {
        string path;
        using (var temp = new TempDirectory("dispose_test"))
        {
            path = temp.Path;
            await Assert.That(Directory.Exists(path)).IsTrue();
        }
        
        await Task.Delay(100); // Give filesystem time
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    [Test]
    public async Task Path_ContainsPrefix()
    {
        using var temp = new TempDirectory("myprefix");
        await Assert.That(temp.Path).Contains("myprefix");
    }
}
CSHARP

# Test: SqliteHelperTests.cs
cat > "$CORE_TESTS_DIR/Infrastructure/SqliteHelperTests.cs" << 'CSHARP'
using Microsoft.Data.Sqlite;
using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Core.Tests.Infrastructure;

public class SqliteHelperTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("sqlite_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    public async Task CreateConnectionString_IncludesDataSource()
    {
        var dbPath = Path.Combine(_temp.Path, "test.db");
        var connStr = SqliteHelper.CreateConnectionString(dbPath);

        await Assert.That(connStr).Contains(dbPath);
    }

    [Test]
    public async Task ApplyRecommendedPragmas_DoesNotThrow()
    {
        var dbPath = Path.Combine(_temp.Path, "pragmas.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await SqliteHelper.ApplyRecommendedPragmasAsync(conn);

        // Verify WAL mode is set
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = await cmd.ExecuteScalarAsync();
        await Assert.That(mode?.ToString()?.ToLower()).IsEqualTo("wal");
    }

    [Test]
    public async Task ExecuteNonQueryAsync_CreatesTable()
    {
        var dbPath = Path.Combine(_temp.Path, "nonquery.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var result = await SqliteHelper.ExecuteNonQueryAsync(
            conn,
            "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");

        await Assert.That(result).IsEqualTo(0); // DDL returns 0

        // Verify table exists
        var count = await SqliteHelper.ExecuteScalarAsync<long>(
            conn,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='test'");
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalarAsync_WithParameters_ReturnsValue()
    {
        var dbPath = Path.Combine(_temp.Path, "scalar.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await SqliteHelper.ExecuteNonQueryAsync(conn, 
            "CREATE TABLE kv (key TEXT PRIMARY KEY, value TEXT)");
        await SqliteHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO kv (key, value) VALUES (@k, @v)",
            new Dictionary<string, object?> { ["@k"] = "test", ["@v"] = "hello" });

        var result = await SqliteHelper.ExecuteScalarAsync<string>(
            conn,
            "SELECT value FROM kv WHERE key = @k",
            new Dictionary<string, object?> { ["@k"] = "test" });

        await Assert.That(result).IsEqualTo("hello");
    }
}
CSHARP

# Test: ActivityExtensionsTests.cs
cat > "$CORE_TESTS_DIR/Telemetry/ActivityExtensionsTests.cs" << 'CSHARP'
using System.Diagnostics;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class ActivityExtensionsTests
{
    private readonly ActivitySource _source = new("TestSource", "1.0.0");

    [Test]
    public async Task RecordException_AddsExceptionEvent()
    {
        using var activity = _source.StartActivity("test");
        var ex = new InvalidOperationException("Test error");

        activity?.RecordException(ex);

        var events = activity?.Events.ToList() ?? [];
        await Assert.That(events.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(events[0].Name).IsEqualTo("exception");
    }

    [Test]
    public async Task RecordException_SetsErrorStatus()
    {
        using var activity = _source.StartActivity("test");
        var ex = new InvalidOperationException("Test error");

        activity?.RecordException(ex);

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task SetSuccess_SetsOkStatus()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetSuccess("All good");

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Ok);
    }

    [Test]
    public async Task SetError_SetsErrorStatus()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetError("Something failed");

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task SetTagIfNotEmpty_AddsTag_WhenValueNotEmpty()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetTagIfNotEmpty("key", "value");

        var tag = activity?.Tags.FirstOrDefault(t => t.Key == "key");
        await Assert.That(tag?.Value).IsEqualTo("value");
    }

    [Test]
    public async Task SetTagIfNotEmpty_DoesNotAddTag_WhenValueEmpty()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetTagIfNotEmpty("key", "");

        var hasTag = activity?.Tags.Any(t => t.Key == "key") ?? false;
        await Assert.That(hasTag).IsFalse();
    }

    [Test]
    public async Task RecordException_HandlesNullActivity()
    {
        Activity? activity = null;
        var ex = new InvalidOperationException("Test");

        // Should not throw
        activity.RecordException(ex);
        
        await Assert.That(true).IsTrue(); // Just verify no exception
    }
}
CSHARP

# Test: JsonTelemetryFileWriterTests.cs
cat > "$CORE_TESTS_DIR/Telemetry/JsonTelemetryFileWriterTests.cs" << 'CSHARP'
using MyImapDownloader.Core.Infrastructure;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class JsonTelemetryFileWriterTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("writer_test");
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private JsonTelemetryFileWriter CreateWriter(
        string? subDir = null,
        string prefix = "test",
        long maxSize = 1024 * 1024)
    {
        var dir = subDir != null
            ? Path.Combine(_temp.Path, subDir)
            : _temp.Path;
        Directory.CreateDirectory(dir);

        var writer = new JsonTelemetryFileWriter(
            dir, prefix, maxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    [Test]
    public async Task Enqueue_CreatesFile_AfterFlush()
    {
        var writer = CreateWriter("enqueue_test");

        writer.Enqueue(new { Message = "test" });
        await writer.FlushAsync();

        var files = Directory.GetFiles(_temp.Path, "*.jsonl", SearchOption.AllDirectories);
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Enqueue_WritesJsonLines()
    {
        var writer = CreateWriter("jsonl_test");

        writer.Enqueue(new { Id = 1, Name = "First" });
        writer.Enqueue(new { Id = 2, Name = "Second" });
        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "jsonl_test"), "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("\"Id\":1");
        await Assert.That(lines[1]).Contains("\"Id\":2");
    }

    [Test]
    public async Task Writer_RotatesFile_WhenSizeExceeded()
    {
        var writer = CreateWriter("rotate_test", maxSize: 100);

        // Write enough data to trigger rotation
        for (int i = 0; i < 10; i++)
        {
            writer.Enqueue(new { Index = i, Data = new string('x', 50) });
            await writer.FlushAsync();
        }

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "rotate_test"), "*.jsonl");
        await Assert.That(files.Length).IsGreaterThan(1);
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        var subDir = Path.Combine(_temp.Path, "dispose_test");
        Directory.CreateDirectory(subDir);

        var writer = new JsonTelemetryFileWriter(
            subDir, "test", 1024 * 1024, TimeSpan.FromMinutes(5));

        writer.Enqueue(new { FinalRecord = true });
        writer.Dispose();

        await Task.Delay(100);

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }
}
CSHARP

# Test: EmailMetadataTests.cs
cat > "$CORE_TESTS_DIR/Data/EmailMetadataTests.cs" << 'CSHARP'
using MyImapDownloader.Core.Data;

namespace MyImapDownloader.Core.Tests.Data;

public class EmailMetadataTests
{
    [Test]
    public async Task EmailMetadata_RequiredProperties_MustBeSet()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.MessageId).IsEqualTo("test@example.com");
    }

    [Test]
    public async Task EmailMetadata_OptionalProperties_CanBeNull()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.Subject).IsNull();
        await Assert.That(metadata.From).IsNull();
        await Assert.That(metadata.To).IsNull();
        await Assert.That(metadata.Date).IsNull();
    }

    [Test]
    public async Task EmailMetadata_WithAllProperties_PreservesValues()
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = new EmailMetadata
        {
            MessageId = "full@example.com",
            Subject = "Test Subject",
            From = "sender@example.com",
            To = "recipient@example.com",
            Cc = "cc@example.com",
            Date = now,
            Folder = "INBOX",
            ArchivedAt = now,
            HasAttachments = true,
            SizeBytes = 1024,
            Account = "user@example.com"
        };

        await Assert.That(metadata.Subject).IsEqualTo("Test Subject");
        await Assert.That(metadata.From).IsEqualTo("sender@example.com");
        await Assert.That(metadata.HasAttachments).IsTrue();
        await Assert.That(metadata.SizeBytes).IsEqualTo(1024);
    }
}
CSHARP

# =============================================================================
# 8. Update solution and project references
# =============================================================================
echo "[8/8] Updating solution and project references..."

# Update MyImapDownloader.slnx to include new projects
if [ -f "$PROJECT_ROOT/MyImapDownloader.slnx" ]; then
    cat > "$PROJECT_ROOT/MyImapDownloader.slnx" << 'SLNX'
<Solution>
  <Folder Name="/Solution Items/">
    <File Path="README.md" />
    <File Path="LICENSE" />
    <File Path=".editorconfig" />
    <File Path="Directory.Build.props" />
    <File Path="Directory.Packages.props" />
    <File Path="global.json" />
  </Folder>
  <Project Path="MyImapDownloader.Core/MyImapDownloader.Core.csproj" />
  <Project Path="MyImapDownloader.Core.Tests/MyImapDownloader.Core.Tests.csproj" />
  <Project Path="MyImapDownloader/MyImapDownloader.csproj" />
  <Project Path="MyImapDownloader.Tests/MyImapDownloader.Tests.csproj" />
  <Project Path="MyEmailSearch/MyEmailSearch.csproj" />
  <Project Path="MyEmailSearch.Tests/MyEmailSearch.Tests.csproj" />
</Solution>
SLNX
fi

echo ""
echo "=========================================="
echo "✅ MyImapDownloader.Core created successfully!"
echo ""
echo "New project structure:"
echo "  MyImapDownloader.Core/"
echo "    ├── Telemetry/"
echo "    │   ├── DiagnosticsConfig.cs"
echo "    │   ├── TelemetryConfiguration.cs"
echo "    │   ├── JsonTelemetryFileWriter.cs"
echo "    │   ├── ActivityExtensions.cs"
echo "    │   ├── JsonFileLogExporter.cs"
echo "    │   ├── JsonFileTraceExporter.cs"
echo "    │   ├── JsonFileMetricsExporter.cs"
echo "    │   ├── TelemetryDirectoryResolver.cs"
echo "    │   └── TelemetryExtensions.cs"
echo "    ├── Configuration/"
echo "    │   └── PathResolver.cs"
echo "    ├── Data/"
echo "    │   └── EmailMetadata.cs"
echo "    └── Infrastructure/"
echo "        ├── SqliteHelper.cs"
echo "        ├── TempDirectory.cs"
echo "        └── TestLogger.cs"
echo ""
echo "  MyImapDownloader.Core.Tests/"
echo "    ├── Telemetry/"
echo "    ├── Configuration/"
echo "    ├── Infrastructure/"
echo "    └── Data/"
echo ""
echo "Next steps:"
echo "  1. Update MyImapDownloader to reference MyImapDownloader.Core"
echo "  2. Update MyEmailSearch to reference MyImapDownloader.Core"
echo "  3. Remove duplicate code from original projects"
echo "  4. Run: dotnet build"
echo "  5. Run: dotnet test"
echo "=========================================="
