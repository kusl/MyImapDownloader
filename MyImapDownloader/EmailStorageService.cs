using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        // Each output directory (account) gets its own isolated database
        _dbPath = Path.Combine(baseDirectory, "index.v1.db");
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_baseDirectory);

        // Fault Tolerance: Try to open/init. If corrupt, back up and start over.
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
        var connStr = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
        _connection = new SqliteConnection(connStr);
        await _connection.OpenAsync(ct);

        // WAL mode for better concurrency
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        using (var trans = await _connection.BeginTransactionAsync(ct))
        {
            var cmd = _connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)trans;
            cmd.CommandText = @"
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
            ";
            await cmd.ExecuteNonQueryAsync(ct);
            await trans.CommitAsync(ct);
        }
    }

    private async Task RecoverDatabaseAsync(CancellationToken ct)
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        // 1. Move corrupt DB aside
        if (File.Exists(_dbPath))
        {
            var backupPath = _dbPath + $".corrupt.{DateTime.UtcNow.Ticks}";
            File.Move(_dbPath, backupPath);
            _logger.LogWarning("Moved corrupt database to {Path}", backupPath);
        }

        // 2. Create fresh DB
        await OpenAndMigrateAsync(ct);

        // 3. Rebuild from disk (Source of Truth)
        _logger.LogInformation("Rebuilding index from disk...");
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("RebuildIndex");
        int count = 0;

        // Scan all .meta.json files
        foreach (var metaFile in Directory.EnumerateFiles(_baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var meta = JsonSerializer.Deserialize<EmailMetadata>(json);
                if (!string.IsNullOrEmpty(meta?.MessageId) && !string.IsNullOrEmpty(meta.Folder))
                {
                    // Insert without validation to recover fast
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

    public async Task<bool> ExistsAsync(string messageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return false;

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Messages WHERE MessageId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", NormalizeMessageId(messageId));
        return (await cmd.ExecuteScalarAsync(ct)) != null;
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
        if (await ExistsAsync(safeId, ct)) return false;

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

            // 3. Parse headers only from the file on disk to get metadata
            // We use MimeKit to parse just the headers, stopping at the body
            using (var fileStream = File.OpenRead(tempPath))
            {
                var parser = new MimeParser(fileStream, MimeFormat.Entity);
                var message = await parser.ParseMessageAsync(ct);

                // If ID was missing in Envelope, try to get it from parsed headers
                if (string.IsNullOrWhiteSpace(messageId) && !string.IsNullOrWhiteSpace(message.MessageId))
                {
                    safeId = NormalizeMessageId(message.MessageId);
                    // Re-check existence with the real ID
                    if (await ExistsAsync(safeId, ct))
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                metadata = new EmailMetadata
                {
                    MessageId = safeId,
                    Subject = message.Subject,
                    From = message.From?.ToString(),
                    To = message.To?.ToString(),
                    Date = message.Date.UtcDateTime,
                    Folder = folderName,
                    ArchivedAt = DateTime.UtcNow,
                    HasAttachments = message.Attachments.Any() // This might require parsing body, careful
                };
            }

            // 4. Move to CUR
            string finalName = GenerateFilename(internalDate, safeId);
            string finalPath = Path.Combine(folderPath, "cur", finalName);

            // Handle race condition if file exists (hash collision or race)
            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
                // Even if file exists on disk, ensure DB knows about it
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

    private static string GenerateFilename(DateTimeOffset date, string safeId)
    {
        string hostname = SanitizeForFilename(Environment.MachineName, 20);
        return $"{date.ToUnixTimeSeconds()}.{safeId}.{hostname}:2,S.eml";
    }

    private static string NormalizeMessageId(string messageId)
    {
        messageId = SanitizeFileName(messageId);
        return messageId?.Trim().Trim('<', '>').ToLowerInvariant() ?? "unknown";
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string SanitizeForFilename(string input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        var sb = new StringBuilder(Math.Min(input.Length, maxLength));
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim('_');
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);

        foreach (char c in input)
        {
            // Explicitly forbid directory separators
            if (c == '/' || c == '\\')
            {
                sb.Append('_');
                continue;
            }

            // Forbid invalid filename chars
            if (Array.IndexOf(invalidChars, c) >= 0)
            {
                sb.Append('_');
                continue;
            }

            sb.Append(c);
        }

        // Avoid pathological filenames
        var result = sb.ToString().Trim('_', ' ');

        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

}
