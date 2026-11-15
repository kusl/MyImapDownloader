using System.Text;

using MyImapDownloader.Imap;

namespace MyImapDownloader.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"downloader_tests_{Guid.NewGuid():N}");
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

public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class Eml
{
    public static byte[] Create(string? messageId, string subject = "Test subject", string body = "Hello world")
    {
        var builder = new StringBuilder();
        if (messageId is not null)
        {
            builder.Append($"Message-ID: <{messageId}>\r\n");
        }

        builder.Append("From: Sender <sender@example.com>\r\n");
        builder.Append("To: Receiver <receiver@example.com>\r\n");
        builder.Append($"Subject: {subject}\r\n");
        builder.Append("Date: Tue, 24 Dec 2024 10:30:00 +0000\r\n");
        builder.Append("MIME-Version: 1.0\r\n");
        builder.Append("Content-Type: text/plain; charset=utf-8\r\n");
        builder.Append("\r\n");
        builder.Append(body).Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}

public sealed class FakeAuthenticationException() : Exception("Invalid credentials");

public sealed record FakeMessage(string? MessageId, string Subject, byte[] Content);

public sealed class FakeFolder(string fullName, uint uidValidity = 1) : IRemoteFolder
{
    public static readonly DateTimeOffset InternalDate = new(2024, 12, 24, 10, 30, 0, TimeSpan.Zero);

    public SortedDictionary<uint, FakeMessage> Messages { get; } = [];

    public Dictionary<uint, Exception> ReadFailures { get; } = [];

    public List<MessageWindow> Searches { get; } = [];

    public List<uint> Reads { get; } = [];

    public uint UidValidity { get; set; } = uidValidity;

    public string FullName => fullName;

    public FakeFolder Add(uint uid, string? messageId, string body = "Hello world")
    {
        var subject = $"Subject {uid}";
        Messages[uid] = new FakeMessage(messageId, subject, Eml.Create(messageId, subject, body));
        return this;
    }

    public Task<uint> OpenAsync(CancellationToken ct) => Task.FromResult(UidValidity);

    public Task<IReadOnlyList<uint>> FindAsync(MessageWindow window, CancellationToken ct)
    {
        Searches.Add(window);
        return Task.FromResult<IReadOnlyList<uint>>(Messages.Keys.Where(uid => uid > window.AfterUid).ToList());
    }

    public Task<IReadOnlyList<RemoteMessage>> DescribeAsync(IReadOnlyList<uint> uids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RemoteMessage>>(uids
            .Where(Messages.ContainsKey)
            .Select(uid => new RemoteMessage(uid, Messages[uid].MessageId, Messages[uid].Subject, InternalDate))
            .ToList());

    public Task<Stream> ReadAsync(uint uid, CancellationToken ct)
    {
        Reads.Add(uid);
        return ReadFailures.Remove(uid, out var failure)
            ? Task.FromException<Stream>(failure)
            : Task.FromResult<Stream>(new MemoryStream(Messages[uid].Content));
    }
}

public sealed class FakeMailServer : IMailServer, IMailSession
{
    public List<FakeFolder> Folders { get; } = [];

    public Queue<Exception> ConnectFailures { get; } = new();

    public int Connections { get; private set; }

    public int Disconnections { get; private set; }

    public Task<IMailSession> ConnectAsync(CancellationToken ct)
    {
        Connections++;
        return ConnectFailures.TryDequeue(out var failure)
            ? Task.FromException<IMailSession>(failure)
            : Task.FromResult<IMailSession>(this);
    }

    public MailFailure Classify(Exception exception) => exception switch
    {
        FakeAuthenticationException => MailFailure.Authentication,
        IOException => MailFailure.Connection,
        _ => MailFailure.Message
    };

    public Task<IReadOnlyList<IRemoteFolder>> GetFoldersAsync(bool allFolders, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<IRemoteFolder>>(allFolders ? [.. Folders] : [.. Folders.Take(1)]);

    public ValueTask DisposeAsync()
    {
        Disconnections++;
        return ValueTask.CompletedTask;
    }
}
