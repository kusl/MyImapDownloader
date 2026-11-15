namespace MyImapDownloader.Imap;

public enum MailFailure
{
    Message,
    Connection,
    Authentication
}

public readonly record struct MessageWindow(uint AfterUid, DateTime? DeliveredAfter, DateTime? DeliveredBefore);

public sealed record RemoteMessage(uint Uid, string? MessageId, string? Subject, DateTimeOffset? InternalDate);

public interface IMailServer
{
    Task<IMailSession> ConnectAsync(CancellationToken ct);

    MailFailure Classify(Exception exception);
}

public interface IMailSession : IAsyncDisposable
{
    Task<IReadOnlyList<IRemoteFolder>> GetFoldersAsync(bool allFolders, CancellationToken ct);
}

public interface IRemoteFolder
{
    string FullName { get; }

    Task<uint> OpenAsync(CancellationToken ct);

    Task<IReadOnlyList<uint>> FindAsync(MessageWindow window, CancellationToken ct);

    Task<IReadOnlyList<RemoteMessage>> DescribeAsync(IReadOnlyList<uint> uids, CancellationToken ct);

    Task<Stream> ReadAsync(uint uid, CancellationToken ct);
}
