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
