using System.Net.Sockets;

using MailKit;
using MailKit.Net.Imap;

namespace MyImapDownloader.Tests;

public class ImapConnectionFailureTests
{
    [Test]
    public async Task IOException_IsConnectionLost()
    {
        var ex = new IOException("The read operation failed", new TimeoutException());

        await Assert.That(ImapConnectionFailure.IsConnectionLost(ex)).IsTrue();
    }

    [Test]
    public async Task SocketException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new SocketException())).IsTrue();
    }

    [Test]
    public async Task TimeoutException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new TimeoutException())).IsTrue();
    }

    [Test]
    public async Task ImapProtocolException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new ImapProtocolException("Unexpected token"))).IsTrue();
    }

    [Test]
    public async Task ServiceNotConnectedException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new ServiceNotConnectedException())).IsTrue();
    }

    [Test]
    public async Task FolderNotOpenException_IsConnectionLost()
    {
        var ex = new FolderNotOpenException("Archives", FolderAccess.ReadOnly);

        await Assert.That(ImapConnectionFailure.IsConnectionLost(ex)).IsTrue();
    }

    [Test]
    public async Task MessageNotFoundException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new MessageNotFoundException("Expunged"))).IsFalse();
    }

    [Test]
    public async Task InvalidOperationException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new InvalidOperationException())).IsFalse();
    }

    [Test]
    public async Task FormatException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new FormatException())).IsFalse();
    }
}
