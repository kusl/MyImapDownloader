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
    private static readonly Counter<long> FilesWritten = DiagnosticsConfig.Meter.CreateCounter<long>(
        "storage.files.written", unit: "files", description: "Number of email files written to disk");
    private static readonly Counter<long> BytesWritten = DiagnosticsConfig.Meter.CreateCounter<long>(
        "storage.bytes.written", unit: "bytes", description: "Total bytes written to disk");
    private static readonly Histogram<double> WriteLatency = DiagnosticsConfig.Meter.CreateHistogram<double>(
        "storage.write.latency", unit: "ms", description: "Time to write email to disk");

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
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("RebuildIndex");
        int count = 0;

        foreach (var metaFile in Directory.EnumerateFiles(_baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var meta = JsonSerializer.Deserialize<EmailMetadata>(json);
                if (!string.IsNullOrEmpty(meta?.MessageId) && !string.IsNullOrEmpty(meta.Folder))
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

        // 1. Double check DB (fast)
        if (await ExistsAsyncNormalized(safeId, ct)) return false;

        string folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        // 2. Stream to TMP file (atomic write pattern)
        string tempName = $"{internalDate.ToUnixTimeSeconds()}.{Guid.NewGuid()}.tmp";
        string tempPath = Path.Combine(folderPath, "tmp", tempName);

        long bytesWritten = 0;
        EmailMetadata? metadata = null;

        try
        {
            // Stream network -> disk directly (Low RAM usage)
            using (var fileStream = File.Create(tempPath))
            {
                await networkStream.CopyToAsync(fileStream, ct);
                bytesWritten = fileStream.Length;
            }

            // 3. FIX: Parse headers ONLY from the file on disk to get metadata
            // This prevents loading large attachments into memory
            using (var fileStream = File.OpenRead(tempPath))
            {
                var parser = new MimeParser(fileStream, MimeFormat.Entity);
                
                // FIX: Use ParseHeadersAsync instead of ParseMessageAsync
                // This only reads headers, not body/attachments - massive memory savings
                var headers = await parser.ParseHeadersAsync(ct);

                // Extract Message-ID from headers if we didn't have it
                var parsedMessageId = headers[HeaderId.MessageId];
                if (string.IsNullOrWhiteSpace(messageId) && !string.IsNullOrWhiteSpace(parsedMessageId))
                {
                    safeId = NormalizeMessageId(parsedMessageId);
                    // Re-check existence with the real ID
                    if (await ExistsAsyncNormalized(safeId, ct))
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                // Build metadata from headers only
                metadata = new EmailMetadata
                {
                    MessageId = safeId,
                    Subject = headers[HeaderId.Subject],
                    From = headers[HeaderId.From],
                    To = headers[HeaderId.To],
                    Date = DateTimeOffset.TryParse(headers[HeaderId.Date], out var d) ? d.UtcDateTime : internalDate.UtcDateTime,
                    Folder = folderName,
                    ArchivedAt = DateTime.UtcNow,
                    // FIX: Cannot determine attachments from headers alone - set to false
                    // This is a trade-off for memory efficiency
                    HasAttachments = false
                };
            }

            // 4. Move to CUR with race condition handling
            string finalName = GenerateFilename(internalDate, safeId);
            string finalPath = Path.Combine(folderPath, "cur", finalName);

            // FIX: Handle race condition with retry and unique suffix
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

            File.Move(tempPath, finalPath);

            // 5. Write Sidecar
            if (metadata != null)
            {
                string metaPath = finalPath + ".meta.json";
                await using var metaStream = File.Create(metaPath);
                await JsonSerializer.SerializeAsync(metaStream, metadata, new JsonSerializerOptions { WriteIndented = true }, ct);
            }

            // 6. Update DB
            await InsertMessageRecordAsync(safeId, folderName, ct);

            sw.Stop();
            FilesWritten.Add(1);
            BytesWritten.Add(bytesWritten);
            WriteLatency.Record(sw.Elapsed.TotalMilliseconds);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save email {Id}", safeId);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private async Task InsertMessageRecordAsync(string messageId, string folder, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Messages (MessageId, Folder, ImportedAt) VALUES (@id, @folder, @date)";
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.Parameters.AddWithValue("@folder", folder);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string GetFolderPath(string folderName)
    {
        string safeName = SanitizeForFilename(folderName, 100);
        return Path.Combine(_baseDirectory, safeName);
    }

    private static void EnsureMaildirStructure(string folderPath)
    {
        Directory.CreateDirectory(Path.Combine(folderPath, "cur"));
        Directory.CreateDirectory(Path.Combine(folderPath, "new"));
        Directory.CreateDirectory(Path.Combine(folderPath, "tmp"));
    }

    public static string GenerateFilename(DateTimeOffset date, string safeId)
    {
        string hostname = SanitizeForFilename(Environment.MachineName, 20);
        return $"{date.ToUnixTimeSeconds()}.{safeId}.{hostname}:2,S.eml";
    }

    public static string NormalizeMessageId(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return "unknown";

        string normalized = SanitizeFileName(messageId)
            .Trim()
            .Trim('<', '>')
            .ToLowerInvariant();

        const int MaxLength = 100;
        if (normalized.Length > MaxLength)
        {
            string hash = ComputeHash(normalized)[..8];
            normalized = normalized[..(MaxLength - 9)] + "_" + hash;
        }

        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    public async Task<bool> ExistsAsyncNormalized(string normalizedMessageId, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Messages WHERE MessageId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", normalizedMessageId);
        return (await cmd.ExecuteScalarAsync(ct)) != null;
    }

    private static string SanitizeFileName(string input)
    {
        return Regex.Replace(input, @"[<>:""/\\|?*\x00-\x1F]", "_");
    }

    public static string SanitizeForFilename(string input, int maxLength)
    {
        var sb = new StringBuilder(maxLength);
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
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
        {
            await _connection.DisposeAsync();
        }
    }
}
