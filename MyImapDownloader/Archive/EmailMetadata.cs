using System.Text.Json.Serialization;

namespace MyImapDownloader.Archive;

public sealed record EmailMetadata
{
    public required string MessageId { get; init; }
    public string? Subject { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTime Date { get; init; }
    public required string Folder { get; init; }
    public DateTime ArchivedAt { get; init; }
    public bool HasAttachments { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EmailMetadata))]
internal partial class EmailMetadataJsonContext : JsonSerializerContext
{
}
