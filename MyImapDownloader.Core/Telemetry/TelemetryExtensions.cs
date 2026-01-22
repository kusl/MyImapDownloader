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
