using OpenTelemetry.Logs;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonLinesLogExporter(JsonLinesFileWriter writer) : JsonLinesExporter<LogRecord>(writer)
{
    protected override void Append(LogRecord log, JsonLinesBuffer buffer) =>
        buffer.Add(
            new LogRecordData
            {
                Timestamp = log.Timestamp != default ? log.Timestamp : DateTime.UtcNow,
                TraceId = log.TraceId != default ? log.TraceId.ToString() : null,
                SpanId = log.SpanId != default ? log.SpanId.ToString() : null,
                LogLevel = log.LogLevel.ToString(),
                CategoryName = log.CategoryName,
                EventId = log.EventId.Id != 0 ? log.EventId.Id : null,
                EventName = log.EventId.Name,
                FormattedMessage = log.FormattedMessage,
                Body = log.Body,
                Attributes = log.Attributes is { Count: > 0 } attributes ? TelemetryTags.ToStrings(attributes) : null,
                Exception = ExceptionInfo.From(log.Exception)
            },
            TelemetryJsonContext.Default.LogRecordData);
}
