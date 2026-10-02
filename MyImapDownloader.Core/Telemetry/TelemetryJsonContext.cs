using System.Text.Json.Serialization;

namespace MyImapDownloader.Core.Telemetry;

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
