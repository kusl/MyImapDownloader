using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using MimeKit;

using MyImapDownloader.Telemetry;

namespace MyImapDownloader;

public class EmailStorageService(ILogger<EmailStorageService> logger, string baseDirectory) : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(baseDirectory, "index.v1.db");
    private SqliteConnection? _connection;

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(baseDirectory);

        try
        {
            await OpenAndMigrateAsync(ct);
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Database corruption detected. Initiating recovery...");
            await RecoverDatabaseAsync(ct);
        }
    }

    private async Task OpenAndMigrateAsync(CancellationToken ct)
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(ct);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

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

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RecoverDatabaseAsync(CancellationToken ct)
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        SqliteConnection.ClearAllPools();

        var suffix = $".corrupt.{DateTime.UtcNow.Ticks}";
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Move(path, path + suffix);
                logger.LogWarning("Moved corrupt database file to {Path}", path + suffix);
            }
        }

        await OpenAndMigrateAsync(ct);

        logger.LogInformation("Rebuilding index from disk...");
        var count = 0;

        foreach (var metaFile in Directory.EnumerateFiles(baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var meta = JsonSerializer.Deserialize(json, EmailMetadataJsonContext.Default.EmailMetadata);
                if (!string.IsNullOrWhiteSpace(meta?.MessageId) &&
                    !string.IsNullOrWhiteSpace(meta.Folder))
                {
                    await InsertMessageRecordAsync(meta.MessageId, meta.Folder, ct);
                    count++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipping malformed meta file {File}: {Error}", metaFile, ex.Message);
            }
        }

        logger.LogInformation("Recovery complete. Re-indexed {Count} emails.", count);
    }

    public async Task<long> GetLastUidAsync(string folderName, long currentValidity, CancellationToken ct)
    {
        if (_connection == null) await InitializeAsync(ct);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT LastUid, UidValidity FROM SyncState WHERE Folder = @folder";
        cmd.Parameters.AddWithValue("@folder", folderName);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var storedValidity = reader.GetInt64(1);
            if (storedValidity == currentValidity)
                return reader.GetInt64(0);

            logger.LogWarning("UIDVALIDITY changed for {Folder}. Resetting cursor.", folderName);
        }

        return 0;
    }

    public async Task UpdateLastUidAsync(string folderName, long lastUid, long validity, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SyncState (Folder, LastUid, UidValidity)
            VALUES (@folder, @uid, @validity)
            ON CONFLICT(Folder) DO UPDATE SET
                LastUid = @uid,
                UidValidity = @validity
            WHERE LastUid < @uid OR UidValidity != @validity;
            """;

        cmd.Parameters.AddWithValue("@folder", folderName);
        cmd.Parameters.AddWithValue("@uid", lastUid);
        cmd.Parameters.AddWithValue("@validity", validity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> SaveStreamAsync(
        Stream networkStream,
        string messageId,
        DateTimeOffset internalDate,
        string folderName,
        CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("SaveStream");
        var sw = Stopwatch.StartNew();

        var normalizedId = string.IsNullOrWhiteSpace(messageId) ? null : NormalizeMessageId(messageId);

        if (normalizedId != null && await ExistsAsyncNormalized(normalizedId, ct))
            return false;

        var folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        var tempPath = Path.Combine(
            folderPath,
            "tmp",
            $"{internalDate.ToUnixTimeSeconds()}.{Guid.NewGuid()}.tmp");

        try
        {
            long bytesWritten;
            using (var fs = File.Create(tempPath))
            {
                await networkStream.CopyToAsync(fs, ct);
                bytesWritten = fs.Length;
            }

            HeaderList headers;
            using (var fs = File.OpenRead(tempPath))
            {
                var parser = new MimeParser(fs, MimeFormat.Entity);
                headers = await parser.ParseHeadersAsync(ct);
            }

            if (normalizedId == null)
            {
                var parsedId = headers[HeaderId.MessageId];
                normalizedId = !string.IsNullOrWhiteSpace(parsedId)
                    ? NormalizeMessageId(parsedId)
                    : await ComputeFileHashAsync(tempPath, ct);

                if (await ExistsAsyncNormalized(normalizedId, ct))
                {
                    File.Delete(tempPath);
                    return false;
                }
            }

            var metadata = new EmailMetadata
            {
                MessageId = normalizedId,
                Subject = headers[HeaderId.Subject],
                From = headers[HeaderId.From],
                To = headers[HeaderId.To],
                Date = DateTimeOffset.TryParse(headers[HeaderId.Date], out var d)
                    ? d.UtcDateTime
                    : internalDate.UtcDateTime,
                Folder = folderName,
                ArchivedAt = DateTime.UtcNow,
                HasAttachments = false
            };

            var safeFileId = SanitizeFilename(normalizedId);
            var finalName = GenerateFilename(internalDate, safeFileId);
            var finalPath = Path.Combine(folderPath, "cur", finalName);

            var attempt = 0;
            while (File.Exists(finalPath) && attempt < 10)
            {
                attempt++;
                finalName = GenerateFilename(internalDate, $"{safeFileId}_{attempt}");
                finalPath = Path.Combine(folderPath, "cur", finalName);
            }

            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
                await InsertMessageRecordAsync(normalizedId, folderName, ct);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath);

            await File.WriteAllTextAsync(
                finalPath + ".meta.json",
                JsonSerializer.Serialize(metadata, EmailMetadataJsonContext.Default.EmailMetadata),
                ct);

            await InsertMessageRecordAsync(normalizedId, folderName, ct);

            DiagnosticsConfig.FilesWritten.Add(1);
            DiagnosticsConfig.BytesWritten.Add(bytesWritten);
            DiagnosticsConfig.WriteLatency.Record(sw.Elapsed.TotalMilliseconds);

            return true;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private async Task InsertMessageRecordAsync(string messageId, string folder, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Messages (MessageId, Folder, ImportedAt) VALUES (@id, @folder, @date)";
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.Parameters.AddWithValue("@folder", folder);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string GetFolderPath(string folderName) =>
        Path.Combine(baseDirectory, SanitizeForFilename(folderName, 100));

    private static void EnsureMaildirStructure(string folderPath)
    {
        Directory.CreateDirectory(Path.Combine(folderPath, "cur"));
        Directory.CreateDirectory(Path.Combine(folderPath, "new"));
        Directory.CreateDirectory(Path.Combine(folderPath, "tmp"));
    }

    public static string GenerateFilename(DateTimeOffset date, string safeId)
    {
        var host = SanitizeForFilename(Environment.MachineName, 20);
        return $"{date.ToUnixTimeSeconds()}.{safeId}.{host}.eml";
    }

    public static string NormalizeMessageId(string messageId)
    {
        var cleaned = Regex.Replace(messageId, @"[<>:""/\\|?*\x00-\x1F]", "_")
            .Trim('<', '>')
            .ToLowerInvariant();

        if (cleaned.Length > 100)
        {
            var hash = ComputeHash(cleaned)[..8];
            cleaned = cleaned[..91] + "_" + hash;
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static string SanitizeFilename(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);

        foreach (var c in input)
            sb.Append(invalid.Contains(c) ? '_' : c);

        return sb.ToString().TrimEnd('.', ' ');
    }

    public async Task<bool> ExistsAsyncNormalized(string id, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Messages WHERE MessageId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        return (await cmd.ExecuteScalarAsync(ct)) != null;
    }

    public static string SanitizeForFilename(string input, int maxLength)
    {
        var sb = new StringBuilder(maxLength);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');

            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim('_');
    }

    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        using var fs = File.OpenRead(path);
        var bytes = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}
