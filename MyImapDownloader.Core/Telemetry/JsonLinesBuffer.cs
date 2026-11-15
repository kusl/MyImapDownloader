using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonLinesBuffer
{
    private readonly ArrayBufferWriter<byte> _bytes = new();

    public ReadOnlySpan<byte> WrittenSpan => _bytes.WrittenSpan;

    public void Add<T>(T record, JsonTypeInfo<T> typeInfo)
    {
        using (var writer = new Utf8JsonWriter(_bytes))
        {
            JsonSerializer.Serialize(writer, record, typeInfo);
        }

        _bytes.Write("\n"u8);
    }
}
