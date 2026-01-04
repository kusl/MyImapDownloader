using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using MimeKit;

using MyImapDownloader.Telemetry;

namespace MyImapDownloader;

public class EmailStorageService : IAsyncDisposable
{
    private readonly ILogger<EmailStorageService> _logger;
    private readonly string _baseDirectory;
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    // Metrics
    private static readonly Counter<long> FilesWritten =
        DiagnosticsConfig.Meter.CreateCounter<long>("storage.files.written");
    private static readonly Counter<long> BytesWritten =
        DiagnosticsConfig.Meter.CreateCounter<long>("storage.bytes.written");
    private static readonly Histogram<double> WriteLatency =
        DiagnosticsConfig.Meter.CreateHistogram<double>("storage.write.latency");

    public EmailStorageService(ILogger<EmailStorageService> logger, string baseDirectory)
    {
        _logger = logger;
        _baseDirectory = baseDirectory;
        _dbPath = Path.Combine(baseDirectory, "index.v1.db");
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_baseDirectory);

        try
        {
            await OpenAndMigrateAsync(ct);
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Database corruption detected. Initiating recovery...");
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
        if (File.Exists(_dbPath))
        {
            var backupPath = _dbPath + $".corrupt.{DateTime.UtcNow.Ticks}";
            File.Move(_dbPath, backupPath);
            _logger.LogWarning("Moved corrupt database to {Path}", backupPath);
        }

        await OpenAndMigrateAsync(ct);

        _logger.LogInformation("Rebuilding index from disk...");
        int count = 0;

        foreach (var metaFile in Directory.EnumerateFiles(_baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var meta = JsonSerializer.Deserialize<EmailMetadata>(json);
                if (!string.IsNullOrWhiteSpace(meta?.MessageId) &&
                    !string.IsNullOrWhiteSpace(meta.Folder))
                {
                    await InsertMessageRecordAsync(meta.MessageId, meta.Folder, ct);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Skipping malformed meta file {File}: {Error}", metaFile, ex.Message);
            }
        }

        _logger.LogInformation("Recovery complete. Re-indexed {Count} emails.", count);
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
            long storedValidity = reader.GetInt64(1);
            if (storedValidity == currentValidity)
            {
                return reader.GetInt64(0);
            }
            else
            {
                _logger.LogWarning("UIDVALIDITY changed for {Folder}. Resetting cursor.", folderName);
                return 0;
            }
        }
        return 0;
    }

    public async Task UpdateLastUidAsync(string folderName, long lastUid, long validity, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SyncState (Folder, LastUid, UidValidity) 
            VALUES (@folder, @uid, @validity)
            ON CONFLICT(Folder) DO UPDATE SET 
                LastUid = @uid, 
                UidValidity = @validity
            WHERE LastUid < @uid OR UidValidity != @validity;";

        cmd.Parameters.AddWithValue("@folder", folderName);
        cmd.Parameters.AddWithValue("@uid", lastUid);
        cmd.Parameters.AddWithValue("@validity", validity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Streams an email to disk. Returns true if saved, false if duplicate.
    /// </summary>
    public async Task<bool> SaveStreamAsync(
        Stream networkStream,
        string messageId,
        DateTimeOffset internalDate,
        string folderName,
        CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("SaveStream");
        var sw = Stopwatch.StartNew();

        string safeId = string.IsNullOrWhiteSpace(messageId)
            ? ComputeHash(internalDate.ToString())
            : NormalizeMessageId(messageId);

        if (await ExistsAsyncNormalized(safeId, ct))
            return false;

        string folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        string tempPath = Path.Combine(
            folderPath,
            "tmp",
            $"{internalDate.ToUnixTimeSeconds()}.{Guid.NewGuid()}.tmp");

        long bytesWritten = 0;
        EmailMetadata? metadata;

        try
        {
            using (var fs = File.Create(tempPath))
            {
                await networkStream.CopyToAsync(fs, ct);
                bytesWritten = fs.Length;
            }

            using (var fs = File.OpenRead(tempPath))
            {
                var parser = new MimeParser(fs, MimeFormat.Entity);
                var headers = await parser.ParseHeadersAsync(ct);

                var parsedId = headers[HeaderId.MessageId];
                if (string.IsNullOrWhiteSpace(messageId) && !string.IsNullOrWhiteSpace(parsedId))
                {
                    safeId = NormalizeMessageId(parsedId);
                    if (await ExistsAsyncNormalized(safeId, ct))
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                metadata = new EmailMetadata
                {
                    MessageId = safeId,
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
            }

            string finalName = GenerateFilename(internalDate, safeId);
            string finalPath = Path.Combine(folderPath, "cur", finalName);

            int attempt = 0;
            while (File.Exists(finalPath) && attempt < 10)
            {
                attempt++;
                finalName = GenerateFilename(internalDate, $"{safeId}_{attempt}");
                finalPath = Path.Combine(folderPath, "cur", finalName);
            }

            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
                await InsertMessageRecordAsync(safeId, folderName, ct);
                return false;
            }

            // 🔑 CRITICAL FIX
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            File.Move(tempPath, finalPath);

            await File.WriteAllTextAsync(
                finalPath + ".meta.json",
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
                ct);

            await InsertMessageRecordAsync(safeId, folderName, ct);

            FilesWritten.Add(1);
            BytesWritten.Add(bytesWritten);
            WriteLatency.Record(sw.Elapsed.TotalMilliseconds);

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
        Path.Combine(_baseDirectory, SanitizeForFilename(folderName, 100));

    private static void EnsureMaildirStructure(string folderPath)
    {
        Directory.CreateDirectory(Path.Combine(folderPath, "cur"));
        Directory.CreateDirectory(Path.Combine(folderPath, "new"));
        Directory.CreateDirectory(Path.Combine(folderPath, "tmp"));
    }

    public static string GenerateFilename(DateTimeOffset date, string safeId)
    {
        string host = SanitizeForFilename(Environment.MachineName, 20);
        return $"{date.ToUnixTimeSeconds()}.{safeId}.{host}:2,S.eml";
    }

    public static string NormalizeMessageId(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return "unknown";

        string cleaned = Regex.Replace(messageId, @"[<>:""/\\|?*\x00-\x1F]", "_")
            .Replace('/', '_')
            .Replace('\\', '_')
            .Trim('<', '>')
            .ToLowerInvariant();

        if (cleaned.Length > 100)
        {
            string hash = ComputeHash(cleaned)[..8];
            cleaned = cleaned[..91] + "_" + hash;
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
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
        foreach (char c in input)
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

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}

