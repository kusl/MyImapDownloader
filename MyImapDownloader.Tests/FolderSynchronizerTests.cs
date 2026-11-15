using Microsoft.Extensions.Logging.Abstractions;

using MyImapDownloader.Archive;
using MyImapDownloader.Sync;

namespace MyImapDownloader.Tests;

public class FolderSynchronizerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeMailServer _server = new();
    private readonly FakeFolder _inbox = new("INBOX");
    private EmailArchive? _archive;

    public async ValueTask DisposeAsync()
    {
        if (_archive is not null)
        {
            await _archive.DisposeAsync();
        }

        _temp.Dispose();
    }

    private static DownloadOptions Options(DateTime? startDate = null) => new()
    {
        Server = "imap.example.com",
        Username = "user",
        Password = "secret",
        OutputDirectory = "unused",
        StartDate = startDate
    };

    private async Task<FolderSynchronizer> CreateAsync()
    {
        _archive ??= await EmailArchive.OpenAsync(
            _temp.Path, NullLoggerFactory.Instance, TimeProvider.System, "testhost", CancellationToken.None);
        return new FolderSynchronizer(
            _archive, _server, NullLogger<FolderSynchronizer>.Instance, TimeProvider.System);
    }

    private int ArchivedCount() =>
        Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "cur"), "*.eml").Length;

    private Task<long> CursorAsync() => _archive!.GetLastUidAsync("INBOX", _inbox.UidValidity, CancellationToken.None);

    [Test]
    public async Task SyncAsync_DownloadsNewMessagesAndAdvancesCursor()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com").Add(3, "three@example.com");
        var synchronizer = await CreateAsync();

        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(ArchivedCount()).IsEqualTo(3);
        await Assert.That(await CursorAsync()).IsEqualTo(3L);
    }

    [Test]
    public async Task SyncAsync_SecondRunOnlyAsksForNewerMessages()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com");
        var synchronizer = await CreateAsync();
        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        _inbox.Add(3, "three@example.com");
        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(_inbox.Searches[1].AfterUid).IsEqualTo(2u);
        await Assert.That(_inbox.Reads.Count).IsEqualTo(3);
        await Assert.That(ArchivedCount()).IsEqualTo(3);
    }

    [Test]
    public async Task SyncAsync_ProcessesMoreThanOneBatch()
    {
        for (uint uid = 1; uid <= 120; uid++)
        {
            _inbox.Add(uid, $"message{uid}@example.com");
        }

        var synchronizer = await CreateAsync();

        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(ArchivedCount()).IsEqualTo(120);
        await Assert.That(await CursorAsync()).IsEqualTo(120L);
    }

    [Test]
    public async Task SyncAsync_FailedMessageHoldsCursorAndIsRetriedNextRun()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com").Add(3, "three@example.com");
        _inbox.ReadFailures[2] = new InvalidOperationException("Message is gone");
        var synchronizer = await CreateAsync();

        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);
        var heldCursor = await CursorAsync();
        var archivedAfterFirstRun = ArchivedCount();

        _inbox.Reads.Clear();
        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(heldCursor).IsEqualTo(1L);
        await Assert.That(archivedAfterFirstRun).IsEqualTo(2);
        await Assert.That(_inbox.Reads.Count).IsEqualTo(1);
        await Assert.That(_inbox.Reads[0]).IsEqualTo(2u);
        await Assert.That(ArchivedCount()).IsEqualTo(3);
        await Assert.That(await CursorAsync()).IsEqualTo(3L);
    }

    [Test]
    public async Task SyncAsync_LostConnectionEndsTheAttempt()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com");
        _inbox.ReadFailures[1] = new IOException("Connection reset");
        var synchronizer = await CreateAsync();

        var act = async () => await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(act);
        await Assert.That(_inbox.Reads.Count).IsEqualTo(1);
        await Assert.That(await CursorAsync()).IsEqualTo(0L);
    }

    [Test]
    public async Task SyncAsync_SkipsMessagesAlreadyInTheArchive()
    {
        _inbox.Add(5, "known@example.com");
        var synchronizer = await CreateAsync();
        await _archive!.StoreAsync(
            new MemoryStream(Eml.Create("known@example.com")),
            "known@example.com",
            FakeFolder.InternalDate,
            "INBOX",
            CancellationToken.None);

        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(_inbox.Reads.Count).IsEqualTo(0);
        await Assert.That(ArchivedCount()).IsEqualTo(1);
        await Assert.That(await CursorAsync()).IsEqualTo(5L);
    }

    [Test]
    public async Task SyncAsync_ChangedUidValidityRescansWithoutDuplicating()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com");
        var synchronizer = await CreateAsync();
        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        _inbox.UidValidity = 2;
        _inbox.Reads.Clear();
        await synchronizer.SyncAsync(_inbox, Options(), CancellationToken.None);

        await Assert.That(_inbox.Searches[1].AfterUid).IsEqualTo(0u);
        await Assert.That(_inbox.Reads.Count).IsEqualTo(0);
        await Assert.That(ArchivedCount()).IsEqualTo(2);
        await Assert.That(await CursorAsync()).IsEqualTo(2L);
    }

    [Test]
    public async Task SyncAsync_DateWindowNeitherUsesNorAdvancesCursor()
    {
        _inbox.Add(1, "one@example.com").Add(2, "two@example.com");
        var synchronizer = await CreateAsync();
        await _archive!.SetLastUidAsync("INBOX", 1, _inbox.UidValidity, CancellationToken.None);

        await synchronizer.SyncAsync(_inbox, Options(new DateTime(2024, 1, 1)), CancellationToken.None);

        await Assert.That(_inbox.Searches[0].AfterUid).IsEqualTo(0u);
        await Assert.That(_inbox.Searches[0].DeliveredAfter).IsEqualTo(new DateTime(2024, 1, 1));
        await Assert.That(ArchivedCount()).IsEqualTo(2);
        await Assert.That(await CursorAsync()).IsEqualTo(1L);
    }
}
