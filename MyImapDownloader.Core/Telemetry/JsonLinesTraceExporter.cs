using System.Diagnostics;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonLinesTraceExporter(JsonLinesFileWriter writer) : JsonLinesExporter<Activity>(writer)
{
    protected override void Append(Activity activity, JsonLinesBuffer buffer) =>
        buffer.Add(
            new TraceRecord
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
                Source = new SourceInfo { Name = activity.Source.Name, Version = activity.Source.Version },
                Tags = TelemetryTags.ToStrings(activity.TagObjects),
                Events = activity.Events
                    .Select(e => new SpanEvent
                    {
                        Name = e.Name,
                        Timestamp = e.Timestamp.UtcDateTime,
                        Attributes = TelemetryTags.ToStrings(e.Tags)
                    })
                    .ToList()
            },
            TelemetryJsonContext.Default.TraceRecord);
}
