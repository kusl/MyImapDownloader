using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonLinesMetricExporter(JsonLinesFileWriter writer) : JsonLinesExporter<Metric>(writer)
{
    protected override void Append(Metric metric, JsonLinesBuffer buffer)
    {
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            buffer.Add(
                new MetricRecord
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
                },
                TelemetryJsonContext.Default.MetricRecord);
        }
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
