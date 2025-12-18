using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Exports OpenTelemetry metrics to JSON files.
/// </summary>
public sealed class JsonFileMetricsExporter : BaseExporter<Metric>
{
    private readonly JsonTelemetryFileWriter _writer;

    public JsonFileMetricsExporter(JsonTelemetryFileWriter writer)
    {
        _writer = writer;
    }

    public override ExportResult Export(in Batch<Metric> batch)
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
                    MeterVersion = metric.MeterVersion,
                    StartTime = point.StartTime.UtcDateTime,
                    EndTime = point.EndTime.UtcDateTime,
                    Tags = ExtractTags(point),
                    Value = ExtractValue(metric, point)
                };

                _writer.Enqueue(record);
            }
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?> ExtractTags(MetricPoint point)
    {
        var tags = new Dictionary<string, string?>();
        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }
        return tags;
    }

    private static MetricValue ExtractValue(Metric metric, MetricPoint point)
    {
        var value = new MetricValue();

        switch (metric.MetricType)
        {
            case MetricType.LongSum:
                value.LongValue = point.GetSumLong();
                break;
            case MetricType.DoubleSum:
                value.DoubleValue = point.GetSumDouble();
                break;
            case MetricType.LongGauge:
                value.LongValue = point.GetGaugeLastValueLong();
                break;
            case MetricType.DoubleGauge:
                value.DoubleValue = point.GetGaugeLastValueDouble();
                break;
            case MetricType.Histogram:
                value.DoubleValue = point.GetHistogramSum();
                value.Count = point.GetHistogramCount();
                value.Buckets = ExtractHistogramBuckets(point);
                break;
            case MetricType.ExponentialHistogram:
                value.DoubleValue = point.GetExponentialHistogramData().Sum;
                value.Count = (long)point.GetExponentialHistogramData().Count;
                break;
        }

        return value;
    }

    private static List<HistogramBucket>? ExtractHistogramBuckets(MetricPoint point)
    {
        var buckets = new List<HistogramBucket>();
        foreach (var bucket in point.GetHistogramBuckets())
        {
            buckets.Add(new HistogramBucket
            {
                ExplicitBound = bucket.ExplicitBound,
                BucketCount = bucket.BucketCount
            });
        }
        return buckets.Count > 0 ? buckets : null;
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
    public string? MeterVersion { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public MetricValue? Value { get; init; }
}

public record MetricValue
{
    public long? LongValue { get; set; }
    public double? DoubleValue { get; set; }
    public long? Count { get; set; }
    public List<HistogramBucket>? Buckets { get; set; }
}

public record HistogramBucket
{
    public double ExplicitBound { get; init; }
    public long BucketCount { get; init; }
}
