using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Scans the email archive directory for .eml files.
/// </summary>
public sealed class ArchiveScanner(ILogger<ArchiveScanner> logger)
{

    /// <summary>
    /// Scans the archive path for all .eml files.
    /// </summary>
    public IEnumerable<string> ScanForEmails(string archivePath)
    {
        if (!Directory.Exists(archivePath))
        {
            logger.LogWarning("Archive path does not exist: {Path}", archivePath);
            yield break;
        }

        logger.LogInformation("Scanning for emails in {Path}", archivePath);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        foreach (var file in Directory.EnumerateFiles(archivePath, "*.eml", options))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Gets the account name from a file path (assumes account folder structure).
    /// </summary>
    public static string? ExtractAccountName(string filePath, string archivePath)
    {
        var relativePath = Path.GetRelativePath(archivePath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        // Expected structure: account_name/folder/cur/file.eml
        return parts.Length >= 2 ? parts[0] : null;
    }

    /// <summary>
    /// Gets the folder name from a file path.
    /// </summary>
    public static string? ExtractFolderName(string filePath, string archivePath)
    {
        var relativePath = Path.GetRelativePath(archivePath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        // Expected structure: account_name/folder/cur/file.eml
        return parts.Length >= 3 ? parts[1] : null;
    }
}
