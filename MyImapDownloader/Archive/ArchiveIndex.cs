using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using MyImapDownloader.Core.Storage;

namespace MyImapDownloader.Archive;

public sealed class ArchiveIndex : IAsyncDisposable
{
    public const string FileName = "index.v1.db";

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Messages (
            MessageId TEXT PRIMARY KEY,
            Folder TEXT NOT NULL,
            ImportedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SyncState (
            Folder TEXT PRIMARY KEY,
            LastUid INTEGER NOT NULL,
            UidValidity INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Messages_Folder ON Messages(Folder);
        """;

    private readonly SqliteConnection _connection;
    private readonly ILogger<ArchiveIndex> _logger;
    private readonly TimeProvider _time;

    private ArchiveIndex(SqliteConnection connection, ILogger<ArchiveIndex> logger, TimeProvider time)
    {
        _connection = connection;
        _logger = logger;
        _time = time;
    }

    public static async Task<ArchiveIndex> OpenAsync(
        string archiveRoot,
        ILogger<ArchiveIndex> logger,
        TimeProvider time,
        CancellationToken ct)
    {
        Directory.CreateDirectory(archiveRoot);
        var path = Path.Combine(archiveRoot, FileName);

        try
        {
            return new ArchiveIndex(await ConnectAsync(path, ct), logger, time);
        }
        catch (SqliteException exception) when (SqliteDatabase.IsCorruption(exception))
        {
            logger.LogError(exception, "Index database is corrupt. Rebuilding it from sidecar metadata.");
        }

        Quarantine(path, logger, time);
        var index = new ArchiveIndex(await ConnectAsync(path, ct), logger, time);
        try
        {
            await index.RebuildFromSidecarsAsync(archiveRoot, ct);
            return index;
        }
        catch
        {
            await index.DisposeAsync();
            throw;
        }
    }

    public async Task<bool> ContainsAsync(string messageId, CancellationToken ct) =>
        await _connection.ScalarAsync(
            "SELECT 1 FROM Messages WHERE MessageId = @id LIMIT 1", ct, [("@id", messageId)]) is not null;

    public Task RecordAsync(string messageId, string folder, CancellationToken ct) =>
        _connection.ExecuteAsync(
            "INSERT OR IGNORE INTO Messages (MessageId, Folder, ImportedAt) VALUES (@id, @folder, @date)",
            ct,
            [("@id", messageId), ("@folder", folder), ("@date", _time.GetUtcNow().UtcDateTime.ToString("O"))]);

    public async Task<long> GetLastUidAsync(string folder, long uidValidity, CancellationToken ct)
    {
        await using var command = _connection.Prepare(
            "SELECT LastUid, UidValidity FROM SyncState WHERE Folder = @folder", [("@folder", folder)]);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return 0;
        }

        if (reader.GetInt64(1) == uidValidity)
        {
            return reader.GetInt64(0);
        }

        _logger.LogWarning("UIDVALIDITY changed for {Folder}. Resetting cursor.", folder);
        return 0;
    }

    public Task SetLastUidAsync(string folder, long lastUid, long uidValidity, CancellationToken ct) =>
        _connection.ExecuteAsync(
            """
            INSERT INTO SyncState (Folder, LastUid, UidValidity)
            VALUES (@folder, @uid, @validity)
            ON CONFLICT(Folder) DO UPDATE SET
                LastUid = @uid,
                UidValidity = @validity
            WHERE LastUid < @uid OR UidValidity != @validity;
            """,
            ct,
            [("@folder", folder), ("@uid", lastUid), ("@validity", uidValidity)]);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static async Task<SqliteConnection> ConnectAsync(string path, CancellationToken ct)
    {
        var connection = await SqliteDatabase.OpenAsync(path, ct);
        try
        {
            await connection.ExecuteAsync(Schema, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void Quarantine(string path, ILogger logger, TimeProvider time)
    {
        var suffix = $".corrupt.{time.GetUtcNow().UtcTicks}";
        foreach (var file in (ReadOnlySpan<string>)[path, path + "-wal", path + "-shm"])
        {
            if (File.Exists(file))
            {
                File.Move(file, file + suffix);
                logger.LogWarning("Moved corrupt database file to {Path}", file + suffix);
            }
        }
    }

    private async Task RebuildFromSidecarsAsync(string archiveRoot, CancellationToken ct)
    {
        _logger.LogInformation("Rebuilding index from disk...");
        var count = 0;

        await using var transaction = await _connection.BeginTransactionAsync(ct);
        foreach (var sidecar in Directory.EnumerateFiles(
            archiveRoot, "*" + MaildirStore.SidecarSuffix, SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(sidecar);
                var metadata = await JsonSerializer.DeserializeAsync(
                    stream, EmailMetadataJsonContext.Default.EmailMetadata, ct);

                if (metadata is { MessageId.Length: > 0, Folder.Length: > 0 })
                {
                    await RecordAsync(metadata.MessageId, metadata.Folder, ct);
                    count++;
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Skipping malformed meta file {File}: {Error}", sidecar, exception.Message);
            }
        }

        await transaction.CommitAsync(ct);
        _logger.LogInformation("Recovery complete. Re-indexed {Count} emails.", count);
    }
}
