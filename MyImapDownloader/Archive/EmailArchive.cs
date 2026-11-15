using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MimeKit;
using MimeKit.Utils;

using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Archive;

public sealed class EmailArchive(
    ArchiveIndex index,
    MaildirStore store,
    ILogger<EmailArchive> logger,
    TimeProvider time) : IAsyncDisposable
{
    private const int MaxHeaderLength = 1024 * 1024;
    private static readonly ParserOptions HeaderParserOptions = new() { MaxHeaderLength = MaxHeaderLength };

    public static async Task<EmailArchive> OpenAsync(
        string root,
        ILoggerFactory loggers,
        TimeProvider time,
        string hostName,
        CancellationToken ct)
    {
        var index = await ArchiveIndex.OpenAsync(root, loggers.CreateLogger<ArchiveIndex>(), time, ct);
        return new EmailArchive(index, new MaildirStore(root, hostName), loggers.CreateLogger<EmailArchive>(), time);
    }

    public Task<long> GetLastUidAsync(string folder, long uidValidity, CancellationToken ct) =>
        index.GetLastUidAsync(folder, uidValidity, ct);

    public Task SetLastUidAsync(string folder, long lastUid, long uidValidity, CancellationToken ct) =>
        index.SetLastUidAsync(folder, lastUid, uidValidity, ct);

    public Task<bool> ContainsAsync(string messageId, CancellationToken ct) =>
        index.ContainsAsync(ArchiveNaming.NormalizeMessageId(messageId), ct);

    public async Task<StoreOutcome> StoreAsync(
        Stream content,
        string? messageId,
        DateTimeOffset internalDate,
        string folderName,
        CancellationToken ct)
    {
        using var activity = DownloaderTelemetry.ActivitySource.StartActivity("SaveStream");
        var started = Stopwatch.GetTimestamp();

        var id = string.IsNullOrWhiteSpace(messageId) ? null : ArchiveNaming.NormalizeMessageId(messageId);
        if (id is not null && await index.ContainsAsync(id, ct))
        {
            return new Duplicate(id);
        }

        var staged = await store.StageAsync(content, folderName, internalDate, ct);
        try
        {
            var headers = await ReadHeadersAsync(staged.Path, ct);

            if (id is null)
            {
                id = headers?[HeaderId.MessageId] is { } header && !string.IsNullOrWhiteSpace(header)
                    ? ArchiveNaming.NormalizeMessageId(header)
                    : await ArchiveNaming.HashFileAsync(staged.Path, ct);

                if (await index.ContainsAsync(id, ct))
                {
                    store.Discard(staged);
                    return new Duplicate(id);
                }
            }

            var metadata = new EmailMetadata
            {
                MessageId = id,
                Subject = headers?[HeaderId.Subject],
                From = headers?[HeaderId.From],
                To = headers?[HeaderId.To],
                Date = DateUtils.TryParse(headers?[HeaderId.Date], out var sent)
                    ? sent.UtcDateTime
                    : internalDate.UtcDateTime,
                Folder = folderName,
                ArchivedAt = time.GetUtcNow().UtcDateTime,
                HasAttachments = false
            };

            var committed = await store.CommitAsync(staged, id, metadata);
            await index.RecordAsync(id, folderName, CancellationToken.None);

            if (!committed.IsNew)
            {
                return new Duplicate(id);
            }

            DownloaderTelemetry.FilesWritten.Add(1);
            DownloaderTelemetry.BytesWritten.Add(staged.Length);
            DownloaderTelemetry.WriteLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return new Stored(id, committed.Path, staged.Length);
        }
        catch
        {
            store.Discard(staged);
            throw;
        }
    }

    public ValueTask DisposeAsync() => index.DisposeAsync();

    private async Task<HeaderList?> ReadHeadersAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await new MimeParser(HeaderParserOptions, stream, MimeFormat.Entity).ParseHeadersAsync(ct);
        }
        catch (FormatException exception)
        {
            logger.LogWarning(exception, "Could not parse headers of {Path}; archiving without header metadata", path);
            return null;
        }
    }
}
