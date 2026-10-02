using System.Text.Json.Serialization;

namespace MyImapDownloader;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EmailMetadata))]
internal partial class EmailMetadataJsonContext : JsonSerializerContext
{
}
