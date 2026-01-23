using System.Text.Json;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an indexed email document.
/// </summary>
public sealed class EmailDocument
{
    public long Id { get; set; }
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
    public long LastModifiedTicks { get; init; }

    // Convenience properties
    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    public DateTimeOffset? DateReceived => DateReceivedUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateReceivedUnix.Value)
        : null;

    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string?> items)
    {
        var list = items.Where(i => i != null).ToList();
        return list.Count > 0 ? JsonSerializer.Serialize(list) : "";
    }
}
