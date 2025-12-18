using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Central configuration for all diagnostics sources used in the application.
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    // ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    // Meter for metrics
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // Counters
    public static readonly Counter<long> EmailsDownloaded = Meter.CreateCounter<long>(
        "emails.downloaded",
        unit: "emails",
        description: "Total number of emails successfully downloaded");

    public static readonly Counter<long> EmailsSkipped = Meter.CreateCounter<long>(
        "emails.skipped",
        unit: "emails",
        description: "Number of emails skipped (duplicates)");

    public static readonly Counter<long> EmailErrors = Meter.CreateCounter<long>(
        "emails.errors",
        unit: "errors",
        description: "Number of email download errors");

    public static readonly Counter<long> BytesDownloaded = Meter.CreateCounter<long>(
        "bytes.downloaded",
        unit: "bytes",
        description: "Total bytes downloaded");

    public static readonly Counter<long> FoldersProcessed = Meter.CreateCounter<long>(
        "folders.processed",
        unit: "folders",
        description: "Number of folders processed");

    public static readonly Counter<long> ConnectionAttempts = Meter.CreateCounter<long>(
        "connection.attempts",
        unit: "attempts",
        description: "Number of IMAP connection attempts");

    public static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "retry.attempts",
        unit: "retries",
        description: "Number of retry attempts due to failures");

    // Histograms
    public static readonly Histogram<double> EmailDownloadDuration = Meter.CreateHistogram<double>(
        "email.download.duration",
        unit: "ms",
        description: "Time taken to download individual emails");

    public static readonly Histogram<double> FolderProcessingDuration = Meter.CreateHistogram<double>(
        "folder.processing.duration",
        unit: "ms",
        description: "Time taken to process entire folders");

    public static readonly Histogram<double> BatchProcessingDuration = Meter.CreateHistogram<double>(
        "batch.processing.duration",
        unit: "ms",
        description: "Time taken to process email batches");

    public static readonly Histogram<long> EmailSize = Meter.CreateHistogram<long>(
        "email.size",
        unit: "bytes",
        description: "Size of downloaded emails");

    // Gauges (using ObservableGauge for current state)
    private static int _activeConnections;
    private static int _queuedEmails;
    private static long _totalEmailsInSession;

    public static readonly ObservableGauge<int> ActiveConnections = Meter.CreateObservableGauge(
        "connections.active",
        () => _activeConnections,
        unit: "connections",
        description: "Number of active IMAP connections");

    public static readonly ObservableGauge<int> QueuedEmails = Meter.CreateObservableGauge(
        "emails.queued",
        () => _queuedEmails,
        unit: "emails",
        description: "Number of emails queued for processing");

    public static readonly ObservableGauge<long> TotalEmailsInSession = Meter.CreateObservableGauge(
        "emails.total.session",
        () => _totalEmailsInSession,
        unit: "emails",
        description: "Total emails processed in current session");

    // Methods to update gauge values
    public static void SetActiveConnections(int count) => _activeConnections = count;
    public static void IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);
    public static void DecrementActiveConnections() => Interlocked.Decrement(ref _activeConnections);
    public static void SetQueuedEmails(int count) => _queuedEmails = count;
    public static void IncrementTotalEmails() => Interlocked.Increment(ref _totalEmailsInSession);
}
