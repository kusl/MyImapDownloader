using Microsoft.Extensions.Logging;

using MimeKit;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

public sealed class EmailParser(string archivePath, ILogger<EmailParser> logger)
{
    private const int BodyPreviewLength = 500;

    public async Task<EmailDocument?> ParseAsync(
        string filePath,
        bool includeFullBody,
        CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
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
                ToAddressesJson = EmailDocument.ToJsonArray(message.To.Mailboxes.Select(m => m.Address)),
                CcAddressesJson = EmailDocument.ToJsonArray(message.Cc.Mailboxes.Select(m => m.Address)),
                BccAddressesJson = EmailDocument.ToJsonArray(message.Bcc.Mailboxes.Select(m => m.Address)),
                Subject = message.Subject,
                DateSentUnix = message.Date != DateTimeOffset.MinValue
                    ? message.Date.ToUnixTimeSeconds()
                    : null,
                Folder = ArchiveScanner.ExtractFolderName(filePath, archivePath),
                Account = ArchiveScanner.ExtractAccountName(filePath, archivePath),
                HasAttachments = attachmentNames.Count > 0,
                AttachmentNamesJson = attachmentNames.Count > 0
                    ? EmailDocument.ToJsonArray(attachmentNames)
                    : null,
                BodyPreview = bodyPreview,
                BodyText = includeFullBody ? bodyText : null,
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastModifiedTicks = fileInfo.LastWriteTimeUtc.Ticks
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse email: {Path}", filePath);
            return null;
        }
    }

    private static string? GetBodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return NormalizeWhitespace(message.TextBody);
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return NormalizeWhitespace(StripHtml(message.HtmlBody));
        }

        return null;
    }

    private static string StripHtml(string html)
    {
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
