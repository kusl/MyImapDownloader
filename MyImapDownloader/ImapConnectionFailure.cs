using System.Net.Sockets;

using MailKit;

namespace MyImapDownloader;

/// <summary>
/// Classifies exceptions that indicate the IMAP connection is no longer usable.
/// </summary>
public static class ImapConnectionFailure
{
    /// <summary>
    /// Returns true when the exception means the connection was lost and the session must reconnect,
    /// as opposed to a failure specific to a single message.
    /// </summary>
    public static bool IsConnectionLost(Exception ex) => ex is
        IOException or
        SocketException or
        TimeoutException or
        ProtocolException or
        ServiceNotConnectedException or
        FolderNotOpenException;
}
