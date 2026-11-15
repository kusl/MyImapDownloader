using System.Net;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using MimeKit;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

public sealed partial class EmailParser(ILogger<EmailParser> logger, TimeProvider time)
{
    private const int BodyPreviewLength = 500;
    private const int MaxHeaderLength = 1024 * 1024;

    private static readonly ParserOptions Options = new() { MaxHeaderLength = MaxHeaderLength };

    public async Task<EmailDocument?> ParseAsync(
        string filePath,
        string archivePath,
        bool includeFullBody,
        CancellationToken ct = default)
    {
        try
        {
            using var message = await MimeMessage.LoadAsync(Options, filePath, ct);

            var sender = message.From.Mailboxes.FirstOrDefault();
            var body = BodyText(message);
            var attachments = message.Attachments
                .OfType<MimePart>()
                .Select(part => part.FileName)
                .OfType<string>()
                .ToList();

            return new EmailDocument
            {
                MessageId = message.MessageId ?? Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                FromAddress = sender?.Address,
                FromName = sender?.Name,
                ToAddressesJson = EmailDocument.ToJsonArray(message.To.Mailboxes.Select(m => m.Address)),
                CcAddressesJson = EmailDocument.ToJsonArray(message.Cc.Mailboxes.Select(m => m.Address)),
                BccAddressesJson = EmailDocument.ToJsonArray(message.Bcc.Mailboxes.Select(m => m.Address)),
                Subject = message.Subject,
                DateSentUnix = message.Date != DateTimeOffset.MinValue ? message.Date.ToUnixTimeSeconds() : null,
                Folder = ArchiveScanner.FolderOf(filePath, archivePath),
                Account = ArchiveScanner.AccountOf(filePath, archivePath),
                HasAttachments = attachments.Count > 0,
                AttachmentNamesJson = attachments.Count > 0 ? EmailDocument.ToJsonArray(attachments) : null,
                BodyPreview = body is null ? null : Truncate(body, BodyPreviewLength),
                BodyText = includeFullBody ? body : null,
                IndexedAtUnix = time.GetUtcNow().ToUnixTimeSeconds(),
                LastModifiedTicks = File.GetLastWriteTimeUtc(filePath).Ticks
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to parse email: {Path}", filePath);
            return null;
        }
    }

    public static string HtmlToText(string html) =>
        WebUtility.HtmlDecode(Tags().Replace(InvisibleElements().Replace(html, " "), " "));

    private static string? BodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return CollapseWhitespace(message.TextBody);
        }

        return string.IsNullOrWhiteSpace(message.HtmlBody) ? null : CollapseWhitespace(HtmlToText(message.HtmlBody));
    }

    private static string CollapseWhitespace(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InvisibleElements();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
