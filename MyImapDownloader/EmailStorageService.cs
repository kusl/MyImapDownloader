using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimeKit;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader;

/// <summary>
/// Stores emails in a Maildir-inspired structure with deduplication and metadata tracking.
/// </summary>
public class EmailStorageService
{
    private readonly ILogger<EmailStorageService> _logger;
    private readonly string _baseDirectory;
    private readonly HashSet<string> _knownMessageIds;
    private readonly string _indexPath;

    // Storage-specific metrics
    private static readonly Counter<long> FilesWritten = DiagnosticsConfig.Meter.CreateCounter<long>(
        "storage.files.written", unit: "files", description: "Number of email files written to disk");
    private static readonly Counter<long> BytesWritten = DiagnosticsConfig.Meter.CreateCounter<long>(
        "storage.bytes.written", unit: "bytes", description: "Total bytes written to disk");
    private static readonly Histogram<double> WriteLatency = DiagnosticsConfig.Meter.CreateHistogram<double>(
        "storage.write.latency", unit: "ms", description: "Time to write email to disk");
    private static readonly Counter<long> DuplicatesDetected = DiagnosticsConfig.Meter.CreateCounter<long>(
        "storage.duplicates.detected", unit: "emails", description: "Number of duplicate emails detected");

    public EmailStorageService(ILogger<EmailStorageService> logger, string baseDirectory)
    {
        _logger = logger;
        _baseDirectory = baseDirectory;
        _indexPath = Path.Combine(baseDirectory, ".email-index.json");
        _knownMessageIds = LoadIndex();
    }

    /// <summary>
    /// Stores an email message, returning true if it was new, false if duplicate.
    /// </summary>
    public async Task<bool> StoreEmailAsync(
        MimeMessage message,
        string folderName,
        CancellationToken ct = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "StoreEmail", ActivityKind.Internal);

        var stopwatch = Stopwatch.StartNew();
        string messageId = GetMessageIdentifier(message);

        activity?.SetTag("message_id", messageId);
        activity?.SetTag("folder", folderName);
        activity?.SetTag("subject", Truncate(message.Subject, 100));

        if (_knownMessageIds.Contains(messageId))
        {
            DuplicatesDetected.Add(1, new KeyValuePair<string, object?>("folder", folderName));
            activity?.SetTag("is_duplicate", true);
            activity?.SetStatus(ActivityStatusCode.Ok, "Duplicate skipped");
            _logger.LogDebug("Skipping duplicate: {MessageId}", messageId);
            return false;
        }

        string folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        string filename = GenerateFilename(message, messageId);
        string tempPath = Path.Combine(folderPath, "tmp", filename);
        string finalPath = Path.Combine(folderPath, "cur", filename);

        activity?.SetTag("file_path", finalPath);

        try
        {
            long bytesWritten;

            // Write to tmp first (atomic write pattern)
            await using (var stream = File.Create(tempPath))
            {
                await message.WriteToAsync(stream, ct);
                bytesWritten = stream.Length;
            }

            // Move to cur (atomic on most filesystems)
            File.Move(tempPath, finalPath, overwrite: false);

            // Write sidecar metadata
            await WriteMetadataAsync(finalPath, message, folderName, ct);

            _knownMessageIds.Add(messageId);

            stopwatch.Stop();

            // Record metrics
            FilesWritten.Add(1, new KeyValuePair<string, object?>("folder", folderName));
            BytesWritten.Add(bytesWritten, new KeyValuePair<string, object?>("folder", folderName));
            WriteLatency.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("folder", folderName));

            activity?.SetTag("bytes_written", bytesWritten);
            activity?.SetTag("write_duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("is_duplicate", false);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation("Stored: {Subject} -> {Path} ({Size} bytes in {Duration}ms)",
                Truncate(message.Subject, 50), finalPath, bytesWritten, stopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (IOException ex) when (File.Exists(finalPath))
        {
            // Race condition - file already exists, treat as duplicate
            activity?.SetTag("race_condition", true);
            activity?.SetStatus(ActivityStatusCode.Ok, "Race condition duplicate");
            _logger.LogDebug("The exception is {message}", ex.Message);
            _logger.LogDebug("File already exists (race): {Path}", finalPath);
            TryDelete(tempPath);
            return false;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            _logger.LogError(ex, "Failed to store email: {MessageId}", messageId);
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Gets a unique identifier for the message, preferring Message-ID header.
    /// </summary>
    private static string GetMessageIdentifier(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return NormalizeMessageId(message.MessageId);
        }

        var sb = new StringBuilder();
        sb.Append(message.Date.ToUniversalTime().ToString("O"));
        sb.Append('|');
        sb.Append(message.From?.ToString() ?? "");
        sb.Append('|');
        sb.Append(message.Subject ?? "");

        return ComputeHash(sb.ToString());
    }

    private static string NormalizeMessageId(string messageId)
    {
        return messageId.Trim().Trim('<', '>').ToLowerInvariant();
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string GenerateFilename(MimeMessage message, string messageId)
    {
        long timestamp = message.Date.ToUnixTimeSeconds();
        string safeId = SanitizeForFilename(messageId, 40);
        string hostname = SanitizeForFilename(Environment.MachineName, 20);

        return $"{timestamp}.{safeId}.{hostname}:2,S.eml";
    }

    private static string SanitizeForFilename(string input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unknown";

        var sb = new StringBuilder(Math.Min(input.Length, maxLength));
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');

            if (sb.Length >= maxLength)
                break;
        }

        return sb.ToString().Trim('_');
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

    private static async Task WriteMetadataAsync(
        string emlPath,
        MimeMessage message,
        string folderName,
        CancellationToken ct)
    {
        var metadata = new EmailMetadata
        {
            MessageId = message.MessageId,
            Subject = message.Subject,
            From = message.From?.ToString(),
            To = message.To?.ToString(),
            Date = message.Date.UtcDateTime,
            Folder = folderName,
            ArchivedAt = DateTime.UtcNow,
            HasAttachments = message.Attachments.Any(),
            AttachmentCount = message.Attachments.Count()
        };

        string metaPath = emlPath + ".meta.json";
        await using var stream = File.Create(metaPath);
        await JsonSerializer.SerializeAsync(stream, metadata,
            new JsonSerializerOptions { WriteIndented = true }, ct);
    }

    private HashSet<string> LoadIndex()
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "LoadIndex", ActivityKind.Internal);

        try
        {
            if (File.Exists(_indexPath))
            {
                string json = File.ReadAllText(_indexPath);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                var result = ids != null ? new HashSet<string>(ids) : [];

                activity?.SetTag("index_count", result.Count);
                activity?.SetTag("source", "file");
                activity?.SetStatus(ActivityStatusCode.Ok);

                return result;
            }
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            _logger.LogWarning(ex, "Could not load index, will rebuild from files");
        }

        activity?.SetTag("source", "rebuild");
        return RebuildIndexFromFiles();
    }

    private HashSet<string> RebuildIndexFromFiles()
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "RebuildIndex", ActivityKind.Internal);

        var stopwatch = Stopwatch.StartNew();
        var ids = new HashSet<string>();

        if (!Directory.Exists(_baseDirectory))
        {
            activity?.SetTag("index_count", 0);
            return ids;
        }

        int filesScanned = 0;
        foreach (var metaFile in Directory.EnumerateFiles(
            _baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            filesScanned++;
            try
            {
                string json = File.ReadAllText(metaFile);
                var meta = JsonSerializer.Deserialize<EmailMetadata>(json);
                if (!string.IsNullOrEmpty(meta?.MessageId))
                    ids.Add(NormalizeMessageId(meta.MessageId));
            }
            catch { /* Skip malformed metadata */ }
        }

        stopwatch.Stop();

        activity?.SetTag("files_scanned", filesScanned);
        activity?.SetTag("index_count", ids.Count);
        activity?.SetTag("rebuild_duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation("Rebuilt index with {Count} known emails from {Files} files in {Duration}ms",
            ids.Count, filesScanned, stopwatch.ElapsedMilliseconds);

        return ids;
    }

    public async Task SaveIndexAsync(CancellationToken ct = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
            "SaveIndex", ActivityKind.Internal);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(_baseDirectory);
            await using var stream = File.Create(_indexPath);
            await JsonSerializer.SerializeAsync(stream, _knownMessageIds.ToList(), cancellationToken: ct);

            stopwatch.Stop();

            activity?.SetTag("index_count", _knownMessageIds.Count);
            activity?.SetTag("save_duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* Ignore cleanup failures */ }
    }

    private static string Truncate(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return "(no subject)";
        return input.Length <= maxLength ? input : input[..(maxLength - 3)] + "...";
    }
}

public record EmailMetadata
{
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTime Date { get; init; }
    public string? Folder { get; init; }
    public DateTime ArchivedAt { get; init; }
    public bool HasAttachments { get; init; }
    public int AttachmentCount { get; init; }
}
