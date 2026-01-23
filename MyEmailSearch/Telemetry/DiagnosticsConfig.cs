using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Telemetry;

namespace MyEmailSearch.Telemetry;

/// <summary>
/// Application-specific diagnostics configuration for MyEmailSearch.
/// </summary>
public static class DiagnosticsConfig
{
    private const string ServiceName = "MyEmailSearch";
    private const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase Base = new(ServiceName, ServiceVersion);

    public static System.Diagnostics.ActivitySource ActivitySource => Base.ActivitySource;
    public static Meter Meter => Base.Meter;

    // Search metrics
    public static readonly Counter<long> SearchesExecuted = Base.CreateCounter<long>(
        "searches.executed", "queries", "Total search queries executed");

    public static readonly Counter<long> SearchErrors = Base.CreateCounter<long>(
        "searches.errors", "errors", "Search query errors");

    public static readonly Histogram<double> SearchDuration = Base.CreateHistogram<double>(
        "search.duration", "ms", "Search query execution time");

    public static readonly Histogram<long> SearchResultCount = Base.CreateHistogram<long>(
        "search.results", "emails", "Number of results per search");

    // Indexing metrics
    public static readonly Counter<long> EmailsIndexed = Base.CreateCounter<long>(
        "indexing.emails", "emails", "Emails indexed");

    public static readonly Counter<long> IndexingErrors = Base.CreateCounter<long>(
        "indexing.errors", "errors", "Indexing errors");

    public static readonly Histogram<double> IndexingDuration = Base.CreateHistogram<double>(
        "indexing.duration", "ms", "Indexing operation duration");
}
