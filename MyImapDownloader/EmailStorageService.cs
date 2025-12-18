using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimeKit;

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
        string messageId = GetMessageIdentifier(message);
        
        if (_knownMessageIds.Contains(messageId))
        {
            _logger.LogDebug("Skipping duplicate: {MessageId}", messageId);
            return false;
        }

        string folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        string filename = GenerateFilename(message, messageId);
        string tempPath = Path.Combine(folderPath, "tmp", filename);
        string finalPath = Path.Combine(folderPath, "cur", filename);

        try
        {
            // Write to tmp first (atomic write pattern)
            await using (var stream = File.Create(tempPath))
            {
                await message.WriteToAsync(stream, ct);
            }

            // Move to cur (atomic on most filesystems)
            File.Move(tempPath, finalPath, overwrite: false);

            // Write sidecar metadata
            await WriteMetadataAsync(finalPath, message, folderName, ct);

            _knownMessageIds.Add(messageId);
            _logger.LogInformation("Stored: {Subject} -> {Path}", 
                Truncate(message.Subject, 50), finalPath);
            
            return true;
        }
        catch (IOException ex) when (File.Exists(finalPath))
        {
            // Race condition - file already exists, treat as duplicate
            _logger.LogDebug("The exception is {message}", ex.Message);
            _logger.LogDebug("File already exists (race): {Path}", finalPath);
            TryDelete(tempPath);
            return false;
        }
        catch (Exception ex)
        {
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
        // Prefer the standard Message-ID header
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return NormalizeMessageId(message.MessageId);
        }

        // Fallback: hash of date + from + subject (for malformed emails)
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
        // Remove angle brackets and normalize
        return messageId.Trim().Trim('<', '>').ToLowerInvariant();
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Generates a Maildir-style filename: timestamp.uniqueid.hostname:2,flags
    /// </summary>
    private static string GenerateFilename(MimeMessage message, string messageId)
    {
        long timestamp = message.Date.ToUnixTimeSeconds();
        string safeId = SanitizeForFilename(messageId, 40);
        string hostname = SanitizeForFilename(Environment.MachineName, 20);
        
        // Maildir format: time.uniqueid.host:2,flags
        // We use 'S' flag (seen) since we're archiving
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
        try
        {
            if (File.Exists(_indexPath))
            {
                string json = File.ReadAllText(_indexPath);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                return ids != null ? new HashSet<string>(ids) : [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load index, will rebuild from files");
        }
        
        return RebuildIndexFromFiles();
    }

    private HashSet<string> RebuildIndexFromFiles()
    {
        var ids = new HashSet<string>();
        
        if (!Directory.Exists(_baseDirectory))
            return ids;

        foreach (var metaFile in Directory.EnumerateFiles(
            _baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                string json = File.ReadAllText(metaFile);
                var meta = JsonSerializer.Deserialize<EmailMetadata>(json);
                if (!string.IsNullOrEmpty(meta?.MessageId))
                    ids.Add(NormalizeMessageId(meta.MessageId));
            }
            catch { /* Skip malformed metadata */ }
        }
        
        _logger.LogInformation("Rebuilt index with {Count} known emails", ids.Count);
        return ids;
    }

    public async Task SaveIndexAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_baseDirectory);
        await using var stream = File.Create(_indexPath);
        await JsonSerializer.SerializeAsync(stream, _knownMessageIds.ToList(), 
            cancellationToken: ct);
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
