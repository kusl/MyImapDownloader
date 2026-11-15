using System.Text;

using MyEmailSearch.Data;

namespace MyEmailSearch.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"search_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public static class Sample
{
    public static EmailDocument Document(
        string filePath,
        string? from = "alice@example.com",
        string? subject = "Quarterly report",
        string? body = null,
        string? to = "[\"bob@example.com\"]",
        long? dateSentUnix = 1_700_000_000,
        string? account = "work",
        string? folder = "INBOX",
        long lastModifiedTicks = 1) => new()
        {
            MessageId = filePath + "@example.com",
            FilePath = filePath,
            FromAddress = from,
            FromName = "Alice",
            ToAddressesJson = to,
            Subject = subject,
            DateSentUnix = dateSentUnix,
            Folder = folder,
            Account = account,
            BodyPreview = body,
            BodyText = body,
            IndexedAtUnix = 1_700_000_100,
            LastModifiedTicks = lastModifiedTicks
        };

    public static string WriteEmail(
        string archivePath,
        string account,
        string folder,
        string name,
        string subject = "Quarterly report",
        string body = "The kafka cluster is healthy.",
        string from = "Alice <alice@example.com>")
    {
        var directory = Path.Combine(archivePath, account, folder, "cur");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".eml");
        File.WriteAllText(path, Email(name + "@example.com", subject, body, from), Encoding.ASCII);
        return path;
    }

    public static string Email(string? messageId, string subject, string body, string from = "Alice <alice@example.com>")
    {
        var builder = new StringBuilder();
        if (messageId is not null)
        {
            builder.Append($"Message-ID: <{messageId}>\r\n");
        }

        builder.Append($"From: {from}\r\n");
        builder.Append("To: Bob <bob@example.com>, carol@example.com\r\n");
        builder.Append("Cc: dave@example.com\r\n");
        builder.Append($"Subject: {subject}\r\n");
        builder.Append("Date: Tue, 24 Dec 2024 10:30:00 +0000\r\n");
        builder.Append("MIME-Version: 1.0\r\n");
        builder.Append("Content-Type: text/plain; charset=utf-8\r\n");
        builder.Append("\r\n");
        builder.Append(body).Append("\r\n");
        return builder.ToString();
    }
}
