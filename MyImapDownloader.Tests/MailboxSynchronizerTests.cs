using Microsoft.Extensions.Logging.Abstractions;

using MyImapDownloader.Archive;
using MyImapDownloader.Sync;

namespace MyImapDownloader.Tests;

public class MailboxSynchronizerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeMailServer _server = new();
    private EmailArchive? _archive;

    public async ValueTask DisposeAsync()
    {
        if (_archive is not null)
        {
            await _archive.DisposeAsync();
        }

        _temp.Dispose();
    }

    private static DownloadOptions Options(bool allFolders = false) => new()
    {
        Server = "imap.example.com",
        Username = "user",
        Password = "secret",
        OutputDirectory = "unused",
        AllFolders = allFolders
    };

    private async Task<MailboxSynchronizer> CreateAsync()
    {
        _archive = await EmailArchive.OpenAsync(
            _temp.Path, NullLoggerFactory.Instance, TimeProvider.System, "testhost", CancellationToken.None);
        var folders = new FolderSynchronizer(
            _archive, _server, NullLogger<FolderSynchronizer>.Instance, TimeProvider.System);
        return new MailboxSynchronizer(
            _server, folders, NullLogger<MailboxSynchronizer>.Instance, _ => TimeSpan.Zero);
    }

    private int ArchivedCount(string folder) =>
        Directory.Exists(Path.Combine(_temp.Path, folder, "cur"))
            ? Directory.GetFiles(Path.Combine(_temp.Path, folder, "cur"), "*.eml").Length
            : 0;

    [Test]
    public async Task RunAsync_SyncsOnlyInboxByDefault()
    {
        _server.Folders.Add(new FakeFolder("INBOX").Add(1, "one@example.com"));
        _server.Folders.Add(new FakeFolder("Archive").Add(1, "two@example.com"));
        var mailbox = await CreateAsync();

        await mailbox.RunAsync(Options(), CancellationToken.None);

        await Assert.That(ArchivedCount("INBOX")).IsEqualTo(1);
        await Assert.That(ArchivedCount("Archive")).IsEqualTo(0);
        await Assert.That(_server.Disconnections).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_SyncsEveryFolderWhenRequested()
    {
        _server.Folders.Add(new FakeFolder("INBOX").Add(1, "one@example.com"));
        _server.Folders.Add(new FakeFolder("Archive").Add(1, "two@example.com"));
        var mailbox = await CreateAsync();

        await mailbox.RunAsync(Options(allFolders: true), CancellationToken.None);

        await Assert.That(ArchivedCount("INBOX")).IsEqualTo(1);
        await Assert.That(ArchivedCount("Archive")).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ReconnectsAfterLostConnectionAndResumes()
    {
        var inbox = new FakeFolder("INBOX").Add(1, "one@example.com").Add(2, "two@example.com");
        inbox.ReadFailures[2] = new IOException("Connection reset");
        _server.Folders.Add(inbox);
        _server.ConnectFailures.Enqueue(new IOException("Host unreachable"));
        var mailbox = await CreateAsync();

        await mailbox.RunAsync(Options(), CancellationToken.None);

        await Assert.That(_server.Connections).IsEqualTo(3);
        await Assert.That(_server.Disconnections).IsEqualTo(2);
        await Assert.That(ArchivedCount("INBOX")).IsEqualTo(2);
    }

    [Test]
    public async Task RunAsync_DoesNotRetryAuthenticationFailures()
    {
        _server.ConnectFailures.Enqueue(new FakeAuthenticationException());
        var mailbox = await CreateAsync();

        var act = async () => await mailbox.RunAsync(Options(), CancellationToken.None);

        await Assert.ThrowsAsync<FakeAuthenticationException>(act);
        await Assert.That(_server.Connections).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_DoesNotRetryCancellation()
    {
        _server.ConnectFailures.Enqueue(new OperationCanceledException());
        var mailbox = await CreateAsync();

        var act = async () => await mailbox.RunAsync(Options(), CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
        await Assert.That(_server.Connections).IsEqualTo(1);
    }

    [Test]
    [Arguments(0, 2)]
    [Arguments(1, 4)]
    [Arguments(7, 256)]
    [Arguments(8, 300)]
    [Arguments(5000, 300)]
    public async Task Backoff_DoublesUpToFiveMinutes(int attempt, int expectedSeconds)
    {
        await Assert.That(MailboxSynchronizer.Backoff(attempt)).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }
}
