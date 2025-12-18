using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Exports OpenTelemetry logs to JSON files.
/// </summary>
public sealed class JsonFileLogExporter : BaseExporter<LogRecord>
{
    private readonly JsonTelemetryFileWriter _writer;

    public JsonFileLogExporter(JsonTelemetryFileWriter writer)
    {
        _writer = writer;
    }

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var log in batch)
        {
            var record = new LogRecordData
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
                Attributes = ExtractAttributes(log),
                Exception = ExtractException(log.Exception)
            };

            _writer.Enqueue(record);
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, object?>? ExtractAttributes(LogRecord log)
    {
        if (log.Attributes == null) return null;

        var attrs = new Dictionary<string, object?>();
        foreach (var attr in log.Attributes)
        {
            attrs[attr.Key] = attr.Value;
        }
        return attrs.Count > 0 ? attrs : null;
    }

    private static ExceptionInfo? ExtractException(Exception? ex)
    {
        if (ex == null) return null;

        return new ExceptionInfo
        {
            Type = ex.GetType().FullName,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = ExtractException(ex.InnerException)
        };
    }
}

public record LogRecordData
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
    public Dictionary<string, object?>? Attributes { get; init; }
    public ExceptionInfo? Exception { get; init; }
}

public record ExceptionInfo
{
    public string? Type { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public ExceptionInfo? InnerException { get; init; }
}
