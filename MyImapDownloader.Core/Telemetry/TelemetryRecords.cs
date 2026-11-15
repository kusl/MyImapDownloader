using System.Globalization;
using System.Text.Json.Serialization;

namespace MyImapDownloader.Core.Telemetry;

public sealed record TraceRecord
{
    public string Type => "trace";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? OperationName { get; init; }
    public string? DisplayName { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    public TimeSpan Duration { get; init; }
    public double DurationMs { get; init; }
    public SourceInfo? Source { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public List<SpanEvent>? Events { get; init; }
}

public sealed record SourceInfo
{
    public string? Name { get; init; }
    public string? Version { get; init; }
}

public sealed record SpanEvent
{
    public string? Name { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
}

public sealed record MetricRecord
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

public sealed record HistogramValue
{
    public long Count { get; init; }
    public double Sum { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
}

public sealed record LogRecordData
{
    public string Type => "log";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? LogLevel { get; init; }
    public string? CategoryName { get; init; }
    public int? EventId { get; init; }
    public string? EventName { get; init; }
    public string? FormattedMessage { get; init; }
    public string? Body { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
    public ExceptionInfo? Exception { get; init; }
}

public sealed record ExceptionInfo
{
    public string? Type { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public ExceptionInfo? InnerException { get; init; }

    public static ExceptionInfo? From(Exception? exception) => exception is null
        ? null
        : new ExceptionInfo
        {
            Type = exception.GetType().FullName,
            Message = exception.Message,
            StackTrace = exception.StackTrace,
            InnerException = From(exception.InnerException)
        };
}

public static class TelemetryTags
{
    public static Dictionary<string, string?> ToStrings(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (key, value) in tags)
        {
            result[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        return result;
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TraceRecord))]
[JsonSerializable(typeof(MetricRecord))]
[JsonSerializable(typeof(LogRecordData))]
[JsonSerializable(typeof(HistogramValue))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
internal partial class TelemetryJsonContext : JsonSerializerContext
{
}
