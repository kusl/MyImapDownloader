using Microsoft.Extensions.Configuration;

namespace MyImapDownloader.Core.Telemetry;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public string ServiceName { get; set; } = "MyImapDownloader";
    public string ServiceVersion { get; set; } = "1.0.0";
    public int MaxFileSizeMB { get; set; } = 25;
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public int FlushIntervalSeconds { get; set; } = 5;
    public int MetricsExportIntervalSeconds { get; set; } = 15;

    public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;

    public static TelemetryOptions From(IConfiguration configuration, string serviceName, string serviceVersion)
    {
        var options = new TelemetryOptions { ServiceName = serviceName, ServiceVersion = serviceVersion };
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }
}
