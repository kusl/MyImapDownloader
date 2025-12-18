using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry with JSON file exporters.
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var config = new TelemetryConfiguration();
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);
        services.AddSingleton(config);

        // Resolve telemetry directory with XDG compliance and fallback
        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);
        
        if (telemetryBaseDir == null)
        {
            // No writable location found - register placeholder services
            // Telemetry will be effectively disabled but app continues normally
            services.AddSingleton<ITelemetryWriterProvider>(new NullTelemetryWriterProvider());
            config.EnableTracing = false;
            config.EnableMetrics = false;
            config.EnableLogging = false;
            
            return services;
        }

        // Update config with resolved directory
        config.OutputDirectory = telemetryBaseDir;

        var tracesDir = Path.Combine(telemetryBaseDir, "traces");
        var metricsDir = Path.Combine(telemetryBaseDir, "metrics");
        var logsDir = Path.Combine(telemetryBaseDir, "logs");

        TryCreateDirectory(tracesDir);
        TryCreateDirectory(metricsDir);
        TryCreateDirectory(logsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        // Create file writers
        JsonTelemetryFileWriter? traceWriter = null;
        JsonTelemetryFileWriter? metricsWriter = null;
        JsonTelemetryFileWriter? logsWriter = null;

        try
        {
            traceWriter = new JsonTelemetryFileWriter(
                tracesDir, "traces", config.MaxFileSizeBytes, flushInterval);
        }
        catch { /* Trace writing disabled */ }

        try
        {
            metricsWriter = new JsonTelemetryFileWriter(
                metricsDir, "metrics", config.MaxFileSizeBytes, flushInterval);
        }
        catch { /* Metrics writing disabled */ }

        try
        {
            logsWriter = new JsonTelemetryFileWriter(
                logsDir, "logs", config.MaxFileSizeBytes, flushInterval);
        }
        catch { /* Log writing disabled */ }

        // Register the writer provider instead of nullable writers directly
        var writerProvider = new TelemetryWriterProvider(traceWriter, metricsWriter, logsWriter);
        services.AddSingleton<ITelemetryWriterProvider>(writerProvider);
        
        // Also register the trace writer directly for Program.cs disposal
        if (traceWriter != null)
        {
            services.AddSingleton(traceWriter);
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: config.ServiceName,
                serviceVersion: config.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["host.name"] = Environment.MachineName,
                ["os.type"] = Environment.OSVersion.Platform.ToString(),
                ["os.version"] = Environment.OSVersion.VersionString,
                ["process.runtime.name"] = ".NET",
                ["process.runtime.version"] = Environment.Version.ToString(),
                ["telemetry.directory"] = telemetryBaseDir
            });

        // Configure OpenTelemetry
        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(config.ServiceName, serviceVersion: config.ServiceVersion));

        if (config.EnableTracing && traceWriter != null)
        {
            otelBuilder.WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(DiagnosticsConfig.ServiceName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddProcessor(new BatchActivityExportProcessor(
                        new JsonFileTraceExporter(traceWriter),
                        maxQueueSize: 2048,
                        scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds,
                        exporterTimeoutMilliseconds: 30000,
                        maxExportBatchSize: 512));
            });
        }

        if (config.EnableMetrics && metricsWriter != null)
        {
            otelBuilder.WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter(DiagnosticsConfig.ServiceName)
                    .AddRuntimeInstrumentation()
                    .AddReader(new PeriodicExportingMetricReader(
                        new JsonFileMetricsExporter(metricsWriter),
                        exportIntervalMilliseconds: config.MetricsExportIntervalSeconds * 1000));
            });
        }

        return services;
    }

    public static ILoggingBuilder AddTelemetryLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        var config = new TelemetryConfiguration();
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);

        if (!config.EnableLogging)
            return builder;

        // Resolve telemetry directory
        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);
        if (telemetryBaseDir == null)
            return builder; // No writable location - skip telemetry logging

        var logsDir = Path.Combine(telemetryBaseDir, "logs");
        if (!TryCreateDirectory(logsDir))
            return builder;

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);
        
        JsonTelemetryFileWriter? logsWriter = null;
        try
        {
            logsWriter = new JsonTelemetryFileWriter(
                logsDir, "logs", config.MaxFileSizeBytes, flushInterval);
        }
        catch
        {
            return builder; // Failed to create writer - skip telemetry logging
        }

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

        return builder;
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
public sealed class TelemetryWriterProvider : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter { get; }
    public JsonTelemetryFileWriter? MetricsWriter { get; }
    public JsonTelemetryFileWriter? LogsWriter { get; }

    public TelemetryWriterProvider(
        JsonTelemetryFileWriter? traceWriter,
        JsonTelemetryFileWriter? metricsWriter,
        JsonTelemetryFileWriter? logsWriter)
    {
        TraceWriter = traceWriter;
        MetricsWriter = metricsWriter;
        LogsWriter = logsWriter;
    }
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
