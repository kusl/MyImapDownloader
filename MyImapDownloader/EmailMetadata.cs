using System;

namespace MyImapDownloader;

public class EmailMetadata
{
    public required string MessageId { get; set; }
    public string? Subject { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTime Date { get; set; }
    public required string Folder { get; set; }
    public DateTime ArchivedAt { get; set; }
    public bool HasAttachments { get; set; }
}
