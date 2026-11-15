using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyImapDownloader.Telemetry;

public static class DownloaderTelemetry
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    public static readonly Counter<long> EmailsDownloaded =
        Meter.CreateCounter<long>("emails.downloaded", "emails", "Total emails downloaded");

    public static readonly Counter<long> RetryAttempts =
        Meter.CreateCounter<long>("retry.attempts", "attempts", "Number of retry attempts");

    public static readonly Counter<long> FilesWritten =
        Meter.CreateCounter<long>("storage.files.written", "files", "Number of email files written");

    public static readonly Counter<long> BytesWritten =
        Meter.CreateCounter<long>("storage.bytes.written", "bytes", "Total bytes written to disk");

    public static readonly Histogram<double> WriteLatency =
        Meter.CreateHistogram<double>("storage.write.latency", "ms", "Disk write latency");
}
