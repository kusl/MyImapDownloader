using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyImapDownloader.Core.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;

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
