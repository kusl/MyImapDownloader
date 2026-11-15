using System.Text.Json;

namespace MyImapDownloader.Archive;

public sealed record StagedMessage(string Path, string FolderPath, long Length, DateTimeOffset InternalDate);

public sealed record CommittedMessage(string Path, bool IsNew);

public sealed class MaildirStore(string root, string hostName)
{
    public const string SidecarSuffix = ".meta.json";

    private const int MaxNameAttempts = 1000;
    private static readonly string[] Subdirectories = ["cur", "new", "tmp"];

    public async Task<StagedMessage> StageAsync(
        Stream content,
        string folderName,
        DateTimeOffset internalDate,
        CancellationToken ct)
    {
        var folderPath = Path.Combine(root, ArchiveNaming.FolderDirectory(folderName));
        foreach (var subdirectory in Subdirectories)
        {
            Directory.CreateDirectory(Path.Combine(folderPath, subdirectory));
        }

        var path = Path.Combine(folderPath, "tmp", $"{internalDate.ToUnixTimeSeconds()}.{Guid.NewGuid()}.tmp");
        try
        {
            await using var file = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await content.CopyToAsync(file, ct);
            await file.FlushAsync(ct);
            file.Flush(flushToDisk: true);
            return new StagedMessage(path, folderPath, file.Length, internalDate);
        }
        catch
        {
            Discard(path);
            throw;
        }
    }

    public async Task<CommittedMessage> CommitAsync(StagedMessage staged, string messageId, EmailMetadata metadata)
    {
        var safeId = ArchiveNaming.SafeFileId(messageId);
        var directory = Path.Combine(staged.FolderPath, "cur");

        for (var attempt = 0; attempt < MaxNameAttempts; attempt++)
        {
            var fileId = attempt == 0 ? safeId : $"{safeId}_{attempt}";
            var path = Path.Combine(directory, ArchiveNaming.FileName(staged.InternalDate, fileId, hostName));

            if (TryMove(staged.Path, path))
            {
                await WriteSidecarAsync(path, staged.FolderPath, metadata);
                return new CommittedMessage(path, IsNew: true);
            }

            if (await HaveSameContentAsync(staged.Path, path))
            {
                await WriteSidecarAsync(path, staged.FolderPath, metadata);
                Discard(staged.Path);
                return new CommittedMessage(path, IsNew: false);
            }
        }

        throw new IOException($"No free file name for message '{messageId}' in '{directory}'.");
    }

    public void Discard(StagedMessage staged) => Discard(staged.Path);

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(destination))
        {
            return false;
        }
    }

    private static async Task<bool> HaveSameContentAsync(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
        {
            return false;
        }

        return await ArchiveNaming.HashFileAsync(first, CancellationToken.None)
            == await ArchiveNaming.HashFileAsync(second, CancellationToken.None);
    }

    private static async Task WriteSidecarAsync(string emailPath, string folderPath, EmailMetadata metadata)
    {
        var sidecarPath = emailPath + SidecarSuffix;
        if (File.Exists(sidecarPath))
        {
            return;
        }

        var staging = Path.Combine(folderPath, "tmp", $"{Guid.NewGuid()}.meta.tmp");
        try
        {
            await using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(file, metadata, EmailMetadataJsonContext.Default.EmailMetadata);
                file.Flush(flushToDisk: true);
            }

            TryMove(staging, sidecarPath);
        }
        finally
        {
            Discard(staging);
        }
    }
}
