using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimeKit;
using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Parses .eml files and extracts structured data for indexing.
/// </summary>
public sealed class EmailParser
{
    private readonly ILogger<EmailParser> _logger;
    private readonly string _archivePath;
    private const int BodyPreviewLength = 500;

    public EmailParser(string archivePath, ILogger<EmailParser> logger)
    {
        _archivePath = archivePath;
        _logger = logger;
    }

    /// <summary>
    /// Parses an .eml file and returns an EmailDocument.
    /// </summary>
    public async Task<EmailDocument?> ParseAsync(
        string filePath,
        bool includeFullBody,
        CancellationToken ct = default)
    {
        try
        {
            var message = await MimeMessage.LoadAsync(filePath, ct).ConfigureAwait(false);

            var bodyText = GetBodyText(message);
            var bodyPreview = bodyText != null
                ? Truncate(bodyText, BodyPreviewLength)
                : null;

            var attachmentNames = message.Attachments
                .Select(a => a is MimePart mp ? mp.FileName : null)
                .Where(n => n != null)
                .Cast<string>()
                .ToList();

            return new EmailDocument
            {
                MessageId = message.MessageId ?? Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                FromAddress = message.From.Mailboxes.FirstOrDefault()?.Address,
                FromName = message.From.Mailboxes.FirstOrDefault()?.Name,
                ToAddressesJson = EmailDocument.ToJsonArray(
                    message.To.Mailboxes.Select(m => m.Address)),
                CcAddressesJson = EmailDocument.ToJsonArray(
                    message.Cc.Mailboxes.Select(m => m.Address)),
                BccAddressesJson = EmailDocument.ToJsonArray(
                    message.Bcc.Mailboxes.Select(m => m.Address)),
                Subject = message.Subject,
                DateSentUnix = message.Date != DateTimeOffset.MinValue
                    ? message.Date.ToUnixTimeSeconds()
                    : null,
                Folder = ArchiveScanner.ExtractFolderName(filePath, _archivePath),
                Account = ArchiveScanner.ExtractAccountName(filePath, _archivePath),
                HasAttachments = attachmentNames.Count > 0,
                AttachmentNamesJson = attachmentNames.Count > 0
                    ? EmailDocument.ToJsonArray(attachmentNames)
                    : null,
                BodyPreview = bodyPreview,
                BodyText = includeFullBody ? bodyText : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse email: {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Attempts to read metadata from sidecar .meta.json file.
    /// </summary>
    public async Task<EmailMetadata?> ReadMetadataAsync(string emlPath, CancellationToken ct)
    {
        var metaPath = emlPath + ".meta.json";
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<EmailMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetBodyText(MimeMessage message)
    {
        // Prefer plain text body
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return NormalizeWhitespace(message.TextBody);
        }

        // Fall back to HTML body stripped of tags
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return NormalizeWhitespace(StripHtml(message.HtmlBody));
        }

        return null;
    }

    private static string StripHtml(string html)
    {
        // Simple HTML tag stripping - for more robust parsing, use a proper library
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&nbsp;", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&amp;", "&");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&lt;", "<");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&gt;", ">");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&quot;", "\"");
        return result;
    }

    private static string NormalizeWhitespace(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}

public sealed record EmailMetadata
{
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? Date { get; init; }
    public long? Uid { get; init; }
}
