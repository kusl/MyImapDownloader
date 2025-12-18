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

        // Create base directory structure
        var tracesDir = Path.Combine(config.OutputDirectory, "traces");
        var metricsDir = Path.Combine(config.OutputDirectory, "metrics");
        var logsDir = Path.Combine(config.OutputDirectory, "logs");

        Directory.CreateDirectory(tracesDir);
        Directory.CreateDirectory(metricsDir);
        Directory.CreateDirectory(logsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        // Create file writers
        var traceWriter = new JsonTelemetryFileWriter(
            tracesDir, "traces", config.MaxFileSizeBytes, flushInterval);
        var metricsWriter = new JsonTelemetryFileWriter(
            metricsDir, "metrics", config.MaxFileSizeBytes, flushInterval);
        var logsWriter = new JsonTelemetryFileWriter(
            logsDir, "logs", config.MaxFileSizeBytes, flushInterval);

        services.AddSingleton(traceWriter);
        services.AddSingleton(metricsWriter);
        services.AddSingleton(logsWriter);

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
                ["process.runtime.version"] = Environment.Version.ToString()
            });

        // Configure OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(config.ServiceName, serviceVersion: config.ServiceVersion))
            .WithTracing(builder =>
            {
                if (config.EnableTracing)
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
                }
            })
            .WithMetrics(builder =>
            {
                if (config.EnableMetrics)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(DiagnosticsConfig.ServiceName)
                        .AddRuntimeInstrumentation()
                        .AddReader(new PeriodicExportingMetricReader(
                            new JsonFileMetricsExporter(metricsWriter),
                            exportIntervalMilliseconds: config.MetricsExportIntervalSeconds * 1000));
                }
            });

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

        var logsDir = Path.Combine(config.OutputDirectory, "logs");
        Directory.CreateDirectory(logsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);
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

        return builder;
    }
}
