using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Base diagnostics configuration for shared telemetry infrastructure.
/// Applications should create their own derived config with application-specific metrics.
/// </summary>
public class DiagnosticsConfigBase
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    public DiagnosticsConfigBase(string serviceName, string serviceVersion)
    {
        ServiceName = serviceName;
        ServiceVersion = serviceVersion;
        _activitySource = new ActivitySource(serviceName, serviceVersion);
        _meter = new Meter(serviceName, serviceVersion);
    }

    public string ServiceName { get; }
    public string ServiceVersion { get; }
    public ActivitySource ActivitySource => _activitySource;
    public Meter Meter => _meter;

    /// <summary>
    /// Creates a counter metric.
    /// </summary>
    public Counter<T> CreateCounter<T>(string name, string? unit = null, string? description = null)
        where T : struct
        => _meter.CreateCounter<T>(name, unit, description);

    /// <summary>
    /// Creates a histogram metric.
    /// </summary>
    public Histogram<T> CreateHistogram<T>(string name, string? unit = null, string? description = null)
        where T : struct
        => _meter.CreateHistogram<T>(name, unit, description);

    /// <summary>
    /// Creates an observable gauge metric.
    /// </summary>
    public ObservableGauge<T> CreateObservableGauge<T>(
        string name,
        Func<T> observeValue,
        string? unit = null,
        string? description = null)
        where T : struct
        => _meter.CreateObservableGauge(name, observeValue, unit, description);
}

/// <summary>
/// Static helper for creating activity spans.
/// </summary>
public static class ActivityHelper
{
    /// <summary>
    /// Starts an activity with the given source and name.
    /// </summary>
    public static Activity? StartActivity(
        ActivitySource source,
        string name,
        ActivityKind kind = ActivityKind.Internal)
        => source.StartActivity(name, kind);
}
