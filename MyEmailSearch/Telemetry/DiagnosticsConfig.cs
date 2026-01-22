using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Telemetry;

namespace MyEmailSearch.Telemetry;

/// <summary>
/// Application-specific diagnostics configuration for MyEmailSearch.
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyEmailSearch";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase _base = new(ServiceName, ServiceVersion);

    public static System.Diagnostics.ActivitySource ActivitySource => _base.ActivitySource;
    public static Meter Meter => _base.Meter;

    // Search metrics
    public static readonly Counter<long> SearchesExecuted = _base.CreateCounter<long>(
        "searches.executed", "queries", "Total search queries executed");

    public static readonly Counter<long> SearchErrors = _base.CreateCounter<long>(
        "searches.errors", "errors", "Search query errors");

    public static readonly Histogram<double> SearchDuration = _base.CreateHistogram<double>(
        "search.duration", "ms", "Search query execution time");

    public static readonly Histogram<long> SearchResultCount = _base.CreateHistogram<long>(
        "search.results", "emails", "Number of results per search");

    // Indexing metrics
    public static readonly Counter<long> EmailsIndexed = _base.CreateCounter<long>(
        "indexing.emails", "emails", "Emails indexed");

    public static readonly Counter<long> IndexingErrors = _base.CreateCounter<long>(
        "indexing.errors", "errors", "Indexing errors");

    public static readonly Histogram<double> IndexingDuration = _base.CreateHistogram<double>(
        "indexing.duration", "ms", "Indexing operation duration");
}
