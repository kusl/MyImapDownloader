using System.Net.Sockets;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Logging;

namespace MyImapDownloader.Imap;

public sealed class MailKitMailServer(DownloadOptions options, ILogger<MailKitMailServer> logger) : IMailServer
{
    private const int TimeoutMilliseconds = 180_000;

    public async Task<IMailSession> ConnectAsync(CancellationToken ct)
    {
        var client = new ImapClient { Timeout = TimeoutMilliseconds };
        try
        {
            logger.LogInformation("Connecting to {Server}:{Port}", options.Server, options.Port);
            await client.ConnectAsync(options.Server, options.Port, SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(options.Username, options.Password, ct);
            return new Session(client, logger);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public MailFailure Classify(Exception exception) => exception switch
    {
        AuthenticationException => MailFailure.Authentication,
        IOException or SocketException or TimeoutException or ProtocolException
            or ServiceNotConnectedException or FolderNotOpenException => MailFailure.Connection,
        _ => MailFailure.Message
    };

    private sealed class Session(ImapClient client, ILogger logger) : IMailSession
    {
        public async Task<IReadOnlyList<IRemoteFolder>> GetFoldersAsync(bool allFolders, CancellationToken ct)
        {
            var inbox = client.Inbox;
            List<IMailFolder> folders = [];

            if (allFolders && client.PersonalNamespaces.Count > 0)
            {
                await CollectAsync(client.GetFolder(client.PersonalNamespaces[0]), folders, ct);
            }

            if (!folders.Contains(inbox))
            {
                folders.Insert(0, inbox);
            }

            return folders.ConvertAll<IRemoteFolder>(folder => new Folder(folder));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "IMAP disconnect failed");
            }
            finally
            {
                client.Dispose();
            }
        }

        private static async Task CollectAsync(IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
        {
            foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
            {
                if ((folder.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0)
                {
                    folders.Add(folder);
                }

                await CollectAsync(folder, folders, ct);
            }
        }
    }

    private sealed class Folder(IMailFolder folder) : IRemoteFolder
    {
        private const MessageSummaryItems SummaryItems =
            MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate;

        public string FullName => folder.FullName;

        public async Task<uint> OpenAsync(CancellationToken ct)
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            return folder.UidValidity;
        }

        public async Task<IReadOnlyList<uint>> FindAsync(MessageWindow window, CancellationToken ct)
        {
            var query = window.AfterUid > 0
                ? SearchQuery.Uids(new UniqueIdRange(new UniqueId(window.AfterUid + 1), UniqueId.MaxValue))
                : SearchQuery.All;

            if (window.DeliveredAfter is { } after)
            {
                query = query.And(SearchQuery.DeliveredAfter(after));
            }

            if (window.DeliveredBefore is { } before)
            {
                query = query.And(SearchQuery.DeliveredBefore(before));
            }

            var uids = await folder.SearchAsync(query, ct);
            return uids.Select(uid => uid.Id).Where(id => id > window.AfterUid).Order().ToList();
        }

        public async Task<IReadOnlyList<RemoteMessage>> DescribeAsync(IReadOnlyList<uint> uids, CancellationToken ct)
        {
            var summaries = await folder.FetchAsync(
                uids.Select(uid => new UniqueId(uid)).ToList(), new FetchRequest(SummaryItems), ct);

            return summaries
                .Select(summary => new RemoteMessage(
                    summary.UniqueId.Id,
                    summary.Envelope?.MessageId,
                    summary.Envelope?.Subject,
                    summary.InternalDate))
                .ToList();
        }

        public Task<Stream> ReadAsync(uint uid, CancellationToken ct) => folder.GetStreamAsync(new UniqueId(uid), ct);
    }
}
