using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyImapDownloader.Core.Telemetry;

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

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        var traceWriter = config.EnableTracing
            ? TryCreateWriter(telemetryBaseDir, "traces", config.MaxFileSizeBytes, flushInterval)
            : null;
        var metricsWriter = config.EnableMetrics
            ? TryCreateWriter(telemetryBaseDir, "metrics", config.MaxFileSizeBytes, flushInterval)
            : null;
        var logsWriter = config.EnableLogging
            ? TryCreateWriter(telemetryBaseDir, "logs", config.MaxFileSizeBytes, flushInterval)
            : null;

        services.AddSingleton<ITelemetryWriterProvider>(
            new TelemetryWriterProvider(traceWriter, metricsWriter, logsWriter));

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: config.ServiceName, serviceVersion: config.ServiceVersion);

        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                if (traceWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(serviceName)
                        .AddProcessor(new BatchActivityExportProcessor(
                            new JsonFileTraceExporter(traceWriter),
                            maxQueueSize: 2048,
                            scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds));
                }
            })
            .WithMetrics(builder =>
            {
                if (metricsWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(serviceName)
                        .AddRuntimeInstrumentation()
                        .AddReader(new PeriodicExportingMetricReader(
                            new JsonFileMetricsExporter(metricsWriter),
                            exportIntervalMilliseconds: config.MetricsExportIntervalSeconds * 1000));
                }
            });

        if (logsWriter != null)
        {
            services.AddLogging(logging => logging.AddOpenTelemetry(options =>
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
            }));
        }

        return services;
    }

    public static void InitializeCoreTelemetry(this IServiceProvider services)
    {
        _ = services.GetService<TracerProvider>();
        _ = services.GetService<MeterProvider>();
    }

    private static JsonTelemetryFileWriter? TryCreateWriter(
        string baseDirectory,
        string kind,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        try
        {
            return new JsonTelemetryFileWriter(
                Path.Combine(baseDirectory, kind), kind, maxFileSizeBytes, flushInterval);
        }
        catch
        {
            return null;
        }
    }
}

public interface ITelemetryWriterProvider : IDisposable
{
    JsonTelemetryFileWriter? TraceWriter { get; }
    JsonTelemetryFileWriter? MetricsWriter { get; }
    JsonTelemetryFileWriter? LogsWriter { get; }
}

public sealed class TelemetryWriterProvider(
    JsonTelemetryFileWriter? traceWriter,
    JsonTelemetryFileWriter? metricsWriter,
    JsonTelemetryFileWriter? logsWriter) : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => traceWriter;
    public JsonTelemetryFileWriter? MetricsWriter => metricsWriter;
    public JsonTelemetryFileWriter? LogsWriter => logsWriter;

    public void Dispose()
    {
        traceWriter?.Dispose();
        metricsWriter?.Dispose();
        logsWriter?.Dispose();
    }
}

public sealed class NullTelemetryWriterProvider : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => null;
    public JsonTelemetryFileWriter? MetricsWriter => null;
    public JsonTelemetryFileWriter? LogsWriter => null;

    public void Dispose()
    {
    }
}
