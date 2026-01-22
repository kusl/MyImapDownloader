using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Exports OpenTelemetry metrics to JSON files.
/// </summary>
public sealed class JsonFileMetricsExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var record = new MetricRecord
                    {
                        Timestamp = point.EndTime.UtcDateTime,
                        MetricName = metric.Name,
                        MetricDescription = metric.Description,
                        MetricUnit = metric.Unit,
                        MetricType = metric.MetricType.ToString(),
                        MeterName = metric.MeterName,
                        StartTime = point.StartTime.UtcDateTime,
                        EndTime = point.EndTime.UtcDateTime,
                        Tags = ExtractTags(point),
                        Value = ExtractValue(metric, point)
                    };

                    writer.Enqueue(record);
                }
            }
        }
        catch
        {
            // Silently ignore export failures
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?>? ExtractTags(MetricPoint point)
    {
        var tags = new Dictionary<string, string?>();
        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }
        return tags.Count > 0 ? tags : null;
    }

    private static object? ExtractValue(Metric metric, MetricPoint point)
    {
        return metric.MetricType switch
        {
            MetricType.LongSum => point.GetSumLong(),
            MetricType.DoubleSum => point.GetSumDouble(),
            MetricType.LongGauge => point.GetGaugeLastValueLong(),
            MetricType.DoubleGauge => point.GetGaugeLastValueDouble(),
            MetricType.Histogram => new
            {
                Count = point.GetHistogramCount(),
                Sum = point.GetHistogramSum(),
                Min = GetHistogramMin(point),
                Max = GetHistogramMax(point)
            },
            _ => null
        };
    }

    private static double? GetHistogramMin(MetricPoint point)
    {
        try
        {
            var prop = point.GetType().GetProperty("HistogramMin");
            return prop?.GetValue(point) as double?;
        }
        catch { return null; }
    }

    private static double? GetHistogramMax(MetricPoint point)
    {
        try
        {
            var prop = point.GetType().GetProperty("HistogramMax");
            return prop?.GetValue(point) as double?;
        }
        catch { return null; }
    }
}

public record MetricRecord
{
    public string Type => "metric";
    public DateTime Timestamp { get; init; }
    public string? MetricName { get; init; }
    public string? MetricDescription { get; init; }
    public string? MetricUnit { get; init; }
    public string? MetricType { get; init; }
    public string? MeterName { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public object? Value { get; init; }
}
