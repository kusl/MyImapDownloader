namespace MyImapDownloader.Core.Data;

/// <summary>
/// Represents metadata for an archived email.
/// This is the common model used by both the downloader and search systems.
/// </summary>
public record EmailMetadata
{
    /// <summary>
    /// The unique message ID from the email headers.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// The email subject line.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The sender address (From header).
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// The recipient addresses (To header).
    /// </summary>
    public string? To { get; init; }

    /// <summary>
    /// The CC addresses.
    /// </summary>
    public string? Cc { get; init; }

    /// <summary>
    /// The date the email was sent.
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// The folder/mailbox where the email is stored.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// When this email was archived.
    /// </summary>
    public DateTimeOffset ArchivedAt { get; init; }

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// The account this email belongs to.
    /// </summary>
    public string? Account { get; init; }
}
