using Microsoft.Extensions.Logging;

using MyImapDownloader.Core.Paths;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonFileTelemetry : IDisposable
{
    private const int MaxQueueSize = 2048;
    private const string RuntimeMeterName = "System.Runtime";

    private readonly TracerProvider? _tracerProvider;
    private readonly MeterProvider? _meterProvider;
    private readonly JsonLinesFileWriter? _logWriter;
    private readonly ResourceBuilder _resource;
    private readonly int _flushIntervalMilliseconds;

    private JsonFileTelemetry(TelemetryOptions options, string sourceName, string? directory, TimeProvider time)
    {
        OutputDirectory = directory;
        _flushIntervalMilliseconds = options.FlushIntervalSeconds * 1000;
        _resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: options.ServiceName, serviceVersion: options.ServiceVersion);

        if (directory is null)
        {
            return;
        }

        if (options.EnableTracing && CreateWriter(directory, "traces", options, time) is { } traceWriter)
        {
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(_resource)
                .AddSource(sourceName)
                .AddProcessor(new BatchActivityExportProcessor(
                    new JsonLinesTraceExporter(traceWriter),
                    maxQueueSize: MaxQueueSize,
                    scheduledDelayMilliseconds: _flushIntervalMilliseconds))
                .Build();
        }

        if (options.EnableMetrics && CreateWriter(directory, "metrics", options, time) is { } metricWriter)
        {
            _meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(_resource)
                .AddMeter(sourceName)
                .AddMeter(RuntimeMeterName)
                .AddReader(new PeriodicExportingMetricReader(
                    new JsonLinesMetricExporter(metricWriter),
                    exportIntervalMilliseconds: options.MetricsExportIntervalSeconds * 1000))
                .Build();
        }

        if (options.EnableLogging)
        {
            _logWriter = CreateWriter(directory, "logs", options, time);
        }
    }

    public string? OutputDirectory { get; }

    public static JsonFileTelemetry Start(
        TelemetryOptions options,
        string sourceName,
        XdgDirectories xdg,
        TimeProvider? time = null) =>
        Start(
            options,
            sourceName,
            TelemetryDirectories.FirstWritable(TelemetryDirectories.Candidates(options.ServiceName, xdg)),
            time);

    public static JsonFileTelemetry Start(
        TelemetryOptions options,
        string sourceName,
        string? directory,
        TimeProvider? time = null) =>
        new(options, sourceName, directory, time ?? TimeProvider.System);

    public void ConfigureLogging(ILoggingBuilder logging)
    {
        if (_logWriter is not { } writer)
        {
            return;
        }

        logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(_resource);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.AddProcessor(new BatchLogRecordExportProcessor(
                new JsonLinesLogExporter(writer),
                maxQueueSize: MaxQueueSize,
                scheduledDelayMilliseconds: _flushIntervalMilliseconds));
        });
    }

    public void Dispose()
    {
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
    }

    private static JsonLinesFileWriter? CreateWriter(
        string directory,
        string kind,
        TelemetryOptions options,
        TimeProvider time)
    {
        try
        {
            return new JsonLinesFileWriter(Path.Combine(directory, kind), kind, options.MaxFileSizeBytes, time);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
