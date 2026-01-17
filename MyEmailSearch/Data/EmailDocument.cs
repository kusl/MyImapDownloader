using System.Text.Json;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an indexed email document.
/// </summary>
public sealed class EmailDocument
{
    public long Id { get; set; }
    public required string MessageId { get; set; }
    public required string FilePath { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ToAddressesJson { get; set; }
    public string? CcAddressesJson { get; set; }
    public string? BccAddressesJson { get; set; }
    public string? Subject { get; set; }
    public long? DateSentUnix { get; set; }
    public long? DateReceivedUnix { get; set; }
    public string? Folder { get; set; }
    public string? Account { get; set; }
    public bool HasAttachments { get; set; }
    public string? AttachmentNamesJson { get; set; }
    public string? BodyPreview { get; set; }
    public string? BodyText { get; set; }
    public long IndexedAtUnix { get; set; }
    public long LastModifiedTicks { get; set; }

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
