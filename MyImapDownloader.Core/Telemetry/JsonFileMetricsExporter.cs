using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Telemetry;

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
                        Tags = ExtractTags(in point),
                        Value = ExtractValue(metric.MetricType, in point)
                    };

                    writer.Enqueue(record, TelemetryJsonContext.Default.MetricRecord);
                }
            }
        }
        catch
        {
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?>? ExtractTags(in MetricPoint point)
    {
        var tags = new Dictionary<string, string?>();
        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }
        return tags.Count > 0 ? tags : null;
    }

    private static object? ExtractValue(MetricType metricType, in MetricPoint point) => metricType switch
    {
        MetricType.LongSum or MetricType.LongSumNonMonotonic => point.GetSumLong(),
        MetricType.DoubleSum or MetricType.DoubleSumNonMonotonic => point.GetSumDouble(),
        MetricType.LongGauge => point.GetGaugeLastValueLong(),
        MetricType.DoubleGauge => point.GetGaugeLastValueDouble(),
        MetricType.Histogram or MetricType.ExponentialHistogram => ExtractHistogram(in point),
        _ => null
    };

    private static HistogramValue ExtractHistogram(in MetricPoint point)
    {
        var hasMinMax = point.TryGetHistogramMinMaxValues(out var min, out var max);
        return new HistogramValue
        {
            Count = point.GetHistogramCount(),
            Sum = point.GetHistogramSum(),
            Min = hasMinMax ? min : null,
            Max = hasMinMax ? max : null
        };
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

public record HistogramValue
{
    public long Count { get; init; }
    public double Sum { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
}
