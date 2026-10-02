using System.Diagnostics;
using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

public static class DiagnosticsConfig
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase Base = new(ServiceName, ServiceVersion);

    public static ActivitySource ActivitySource => Base.ActivitySource;
    public static Meter Meter => Base.Meter;

    public static readonly Counter<long> EmailsDownloaded = Base.CreateCounter<long>(
        "emails.downloaded", "emails", "Total emails downloaded");

    public static readonly Counter<long> RetryAttempts = Base.CreateCounter<long>(
        "retry.attempts", "attempts", "Number of retry attempts");

    public static readonly Counter<long> FilesWritten = Base.CreateCounter<long>(
        "storage.files.written", "files", "Number of email files written");

    public static readonly Counter<long> BytesWritten = Base.CreateCounter<long>(
        "storage.bytes.written", "bytes", "Total bytes written to disk");

    public static readonly Histogram<double> WriteLatency = Base.CreateHistogram<double>(
        "storage.write.latency", "ms", "Disk write latency");
}
