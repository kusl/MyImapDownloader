using System.Diagnostics;
using OpenTelemetry;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Exports OpenTelemetry traces to JSON files.
/// </summary>
public sealed class JsonFileTraceExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var activity in batch)
            {
                var record = new TraceRecord
                {
                    Timestamp = activity.StartTimeUtc,
                    TraceId = activity.TraceId.ToString(),
                    SpanId = activity.SpanId.ToString(),
                    ParentSpanId = activity.ParentSpanId.ToString(),
                    OperationName = activity.OperationName,
                    DisplayName = activity.DisplayName,
                    Kind = activity.Kind.ToString(),
                    Status = activity.Status.ToString(),
                    StatusDescription = activity.StatusDescription,
                    Duration = activity.Duration,
                    DurationMs = activity.Duration.TotalMilliseconds,
                    Source = new SourceInfo
                    {
                        Name = activity.Source.Name,
                        Version = activity.Source.Version
                    },
                    Tags = activity.Tags.ToDictionary(t => t.Key, t => t.Value),
                    Events = activity.Events.Select(e => new SpanEvent
                    {
                        Name = e.Name,
                        Timestamp = e.Timestamp.UtcDateTime,
                        Attributes = e.Tags.ToDictionary(t => t.Key, t => t.Value?.ToString())
                    }).ToList()
                };

                writer.Enqueue(record);
            }
        }
        catch
        {
            // Silently ignore export failures
        }

        return ExportResult.Success;
    }
}

public record TraceRecord
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

public record SourceInfo
{
    public string? Name { get; init; }
    public string? Version { get; init; }
}

public record SpanEvent
{
    public string? Name { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
}
