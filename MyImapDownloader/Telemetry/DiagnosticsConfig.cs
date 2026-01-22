using System.Diagnostics.Metrics;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Application-specific diagnostics configuration for MyImapDownloader.
/// Extends the core telemetry infrastructure with email-specific metrics.
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase _base = new(ServiceName, ServiceVersion);

    public static System.Diagnostics.ActivitySource ActivitySource => _base.ActivitySource;
    public static Meter Meter => _base.Meter;

    // Email download metrics
    public static readonly Counter<long> EmailsDownloaded = _base.CreateCounter<long>(
        "emails.downloaded", "emails", "Total emails downloaded");

    public static readonly Counter<long> BytesDownloaded = _base.CreateCounter<long>(
        "bytes.downloaded", "bytes", "Total bytes downloaded");

    public static readonly Histogram<double> DownloadLatency = _base.CreateHistogram<double>(
        "download.latency", "ms", "Email download latency");

    public static readonly Counter<long> RetryAttempts = _base.CreateCounter<long>(
        "retry.attempts", "attempts", "Number of retry attempts");

    // Storage metrics
    public static readonly Counter<long> FilesWritten = _base.CreateCounter<long>(
        "storage.files.written", "files", "Number of email files written");

    public static readonly Counter<long> BytesWritten = _base.CreateCounter<long>(
        "storage.bytes.written", "bytes", "Total bytes written to disk");

    public static readonly Histogram<double> WriteLatency = _base.CreateHistogram<double>(
        "storage.write.latency", "ms", "Disk write latency");

    // Connection metrics
    private static int _activeConnections;
    private static int _queuedEmails;
    private static long _totalEmailsInSession;

    public static readonly ObservableGauge<int> ActiveConnections = _base.Meter.CreateObservableGauge(
        "connections.active", () => _activeConnections, "connections", "Active IMAP connections");

    public static readonly ObservableGauge<int> QueuedEmails = _base.Meter.CreateObservableGauge(
        "emails.queued", () => _queuedEmails, "emails", "Emails queued for processing");

    public static readonly ObservableGauge<long> TotalEmailsInSession = _base.Meter.CreateObservableGauge(
        "emails.total.session", () => _totalEmailsInSession, "emails", "Total emails in session");

    public static void SetActiveConnections(int count) => _activeConnections = count;
    public static void IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);
    public static void DecrementActiveConnections() => Interlocked.Decrement(ref _activeConnections);
    public static void SetQueuedEmails(int count) => _queuedEmails = count;
    public static void IncrementTotalEmails() => Interlocked.Increment(ref _totalEmailsInSession);
}
