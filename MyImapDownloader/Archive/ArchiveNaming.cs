using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MyImapDownloader.Archive;

public static partial class ArchiveNaming
{
    private const int MaxMessageIdLength = 100;
    private const int MaxFolderLength = 100;
    private const int MaxHostLength = 20;

    public static string NormalizeMessageId(string messageId)
    {
        var cleaned = UnsafeMessageIdCharacters().Replace(messageId, "_").ToLowerInvariant();

        if (cleaned.Length > MaxMessageIdLength)
        {
            cleaned = $"{cleaned[..91]}_{Hash(cleaned)[..8]}";
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    public static string SafeFileId(string normalizedMessageId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(normalizedMessageId.Length);
        foreach (var c in normalizedMessageId)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.ToString().TrimEnd('.', ' ');
    }

    public static string FolderDirectory(string folderName) => Sanitize(folderName, MaxFolderLength);

    public static string FileName(DateTimeOffset internalDate, string safeFileId, string hostName) =>
        $"{internalDate.ToUnixTimeSeconds()}.{safeFileId}.{Sanitize(hostName, MaxHostLength)}.eml";

    public static string Sanitize(string input, int maxLength)
    {
        var builder = new StringBuilder(maxLength);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }

            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('_');
    }

    public static string Hash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    [GeneratedRegex("""[<>:"/\\|?*\x00-\x1F]""")]
    private static partial Regex UnsafeMessageIdCharacters();
}
