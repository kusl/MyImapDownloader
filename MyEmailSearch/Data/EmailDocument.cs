using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an email document stored in the search index.
/// </summary>
public sealed record EmailDocument
{
    public long Id { get; init; }
    public required string MessageId { get; init; }
    public required string FilePath { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? ToAddressesJson { get; init; }
    public string? CcAddressesJson { get; init; }
    public string? BccAddressesJson { get; init; }
    public string? Subject { get; init; }
    public long? DateSentUnix { get; init; }
    public long? DateReceivedUnix { get; init; }
    public string? Folder { get; init; }
    public string? Account { get; init; }
    public bool HasAttachments { get; init; }
    public string? AttachmentNamesJson { get; init; }
    public string? BodyPreview { get; init; }
    public string? BodyText { get; init; }
    public long IndexedAtUnix { get; init; }
    
    // Tracks the file's modification time to skip unnecessary re-indexing
    public long LastModifiedTicks { get; init; }

    // Computed properties
    [JsonIgnore]
    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    [JsonIgnore]
    public DateTimeOffset? DateReceived => DateReceivedUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateReceivedUnix.Value)
        : null;

    [JsonIgnore]
    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);
    
    [JsonIgnore]
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);
    
    [JsonIgnore]
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string>? items)
    {
        if (items == null) return "[]";
        return JsonSerializer.Serialize(items.ToList());
    }
}
