using System;
using System.Collections.Generic;

using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Telemetry;

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
                        MeterVersion = metric.MeterVersion,
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
            // Silently ignore export failures - telemetry should never crash the app
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

        try
        {
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
                    value.Count = (long)point.GetHistogramCount();
                    value.Buckets = ExtractHistogramBuckets(point);
                    break;
                case MetricType.ExponentialHistogram:
                    // ExponentialHistogramData in OpenTelemetry 1.14.0 uses different API
                    // Access count and sum through the MetricPoint directly
                    var expHistData = point.GetExponentialHistogramData();
                    value.Count = GetExponentialHistogramCount(expHistData);
                    value.DoubleValue = GetExponentialHistogramSum(expHistData);
                    break;
            }
        }
        catch
        {
            // If extraction fails, return partial data
        }

        return value;
    }

    private static long GetExponentialHistogramCount(ExponentialHistogramData data)
    {
        try
        {
            // Try accessing via reflection for API compatibility
            var countProperty = typeof(ExponentialHistogramData).GetProperty("Count");
            if (countProperty != null)
            {
                var val = countProperty.GetValue(data);
                if (val is long l) return l;
                if (val is ulong ul) return (long)ul;
                if (val is int i) return i;
            }

            // Try ZeroCount + positive/negative bucket counts as fallback
            var zeroCountProp = typeof(ExponentialHistogramData).GetProperty("ZeroCount");
            if (zeroCountProp != null)
            {
                var zeroCount = Convert.ToInt64(zeroCountProp.GetValue(data) ?? 0);
                return zeroCount; // This is a partial count but better than nothing
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return 0;
    }

    private static double GetExponentialHistogramSum(ExponentialHistogramData data)
    {
        try
        {
            // Try to access Sum via reflection for API compatibility
            var sumProperty = typeof(ExponentialHistogramData).GetProperty("Sum");
            if (sumProperty != null)
            {
                var val = sumProperty.GetValue(data);
                if (val is double d) return d;
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return 0.0;
    }

    private static List<HistogramBucket>? ExtractHistogramBuckets(MetricPoint point)
    {
        try
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
        catch
        {
            return null;
        }
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
