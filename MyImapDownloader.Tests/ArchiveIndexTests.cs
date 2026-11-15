using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using MyImapDownloader.Archive;

namespace MyImapDownloader.Tests;

public class ArchiveIndexTests : IDisposable
{
    private const string LegacySchema = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;

        CREATE TABLE IF NOT EXISTS Messages (
            MessageId TEXT PRIMARY KEY,
            Folder TEXT NOT NULL,
            ImportedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SyncState (
            Folder TEXT PRIMARY KEY,
            LastUid INTEGER NOT NULL,
            UidValidity INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Messages_Folder ON Messages(Folder);

        INSERT INTO Messages (MessageId, Folder, ImportedAt) VALUES ('legacy@example.com', 'INBOX', '2025-01-01T00:00:00.0000000Z');
        INSERT INTO SyncState (Folder, LastUid, UidValidity) VALUES ('INBOX', 41, 7);
        """;

    private readonly TempDirectory _temp = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    private string DatabasePath => Path.Combine(_temp.Path, ArchiveIndex.FileName);

    private Task<ArchiveIndex> OpenAsync() =>
        ArchiveIndex.OpenAsync(_temp.Path, NullLogger<ArchiveIndex>.Instance, _time, CancellationToken.None);

    private void WriteSidecar(string folder, string name, string json)
    {
        var directory = Path.Combine(_temp.Path, folder, "cur");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".eml.meta.json"), json);
    }

    [Test]
    public async Task OpenAsync_CreatesIndexFile()
    {
        await using var index = await OpenAsync();

        await Assert.That(File.Exists(DatabasePath)).IsTrue();
    }

    [Test]
    public async Task RecordAsync_MakesMessageKnown()
    {
        await using var index = await OpenAsync();

        await index.RecordAsync("a@example.com", "INBOX", CancellationToken.None);
        await index.RecordAsync("a@example.com", "Archive", CancellationToken.None);

        await Assert.That(await index.ContainsAsync("a@example.com", CancellationToken.None)).IsTrue();
        await Assert.That(await index.ContainsAsync("b@example.com", CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task LastUid_IsZeroForUnknownFolder()
    {
        await using var index = await OpenAsync();

        await Assert.That(await index.GetLastUidAsync("INBOX", 1, CancellationToken.None)).IsEqualTo(0L);
    }

    [Test]
    public async Task LastUid_RoundTripsAndSurvivesReopen()
    {
        await using (var index = await OpenAsync())
        {
            await index.SetLastUidAsync("INBOX", 42, 7, CancellationToken.None);
        }

        await using var reopened = await OpenAsync();

        await Assert.That(await reopened.GetLastUidAsync("INBOX", 7, CancellationToken.None)).IsEqualTo(42L);
    }

    [Test]
    public async Task LastUid_NeverMovesBackwardsForSameValidity()
    {
        await using var index = await OpenAsync();

        await index.SetLastUidAsync("INBOX", 42, 7, CancellationToken.None);
        await index.SetLastUidAsync("INBOX", 10, 7, CancellationToken.None);

        await Assert.That(await index.GetLastUidAsync("INBOX", 7, CancellationToken.None)).IsEqualTo(42L);
    }

    [Test]
    public async Task LastUid_ResetsWhenUidValidityChanges()
    {
        await using var index = await OpenAsync();
        await index.SetLastUidAsync("INBOX", 42, 7, CancellationToken.None);

        var afterChange = await index.GetLastUidAsync("INBOX", 8, CancellationToken.None);
        await index.SetLastUidAsync("INBOX", 3, 8, CancellationToken.None);

        await Assert.That(afterChange).IsEqualTo(0L);
        await Assert.That(await index.GetLastUidAsync("INBOX", 8, CancellationToken.None)).IsEqualTo(3L);
    }

    [Test]
    public async Task OpenAsync_KeepsDataWrittenByEarlierVersions()
    {
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = LegacySchema;
            await command.ExecuteNonQueryAsync();
        }

        await using var index = await OpenAsync();

        await Assert.That(await index.ContainsAsync("legacy@example.com", CancellationToken.None)).IsTrue();
        await Assert.That(await index.GetLastUidAsync("INBOX", 7, CancellationToken.None)).IsEqualTo(41L);
        await Assert.That(Directory.GetFiles(_temp.Path, "*.corrupt.*").Length).IsEqualTo(0);
    }

    [Test]
    public async Task OpenAsync_QuarantinesCorruptIndexAndRebuildsFromSidecars()
    {
        await File.WriteAllTextAsync(DatabasePath, new string('x', 8192));
        WriteSidecar("INBOX", "1", """
            {
              "MessageId": "one@example.com",
              "Subject": "One",
              "From": "a@example.com",
              "To": "b@example.com",
              "Date": "2025-12-24T10:30:00Z",
              "Folder": "INBOX",
              "ArchivedAt": "2025-12-24T15:45:32Z",
              "HasAttachments": false
            }
            """);
        WriteSidecar("Archive", "2", """{ "MessageId": "two@example.com", "Folder": "Archive" }""");
        WriteSidecar("Archive", "3", "not json");
        WriteSidecar("Archive", "4", """{ "Subject": "missing required members" }""");

        await using var index = await OpenAsync();

        await Assert.That(Directory.GetFiles(_temp.Path, ArchiveIndex.FileName + ".corrupt.*").Length).IsEqualTo(1);
        await Assert.That(await index.ContainsAsync("one@example.com", CancellationToken.None)).IsTrue();
        await Assert.That(await index.ContainsAsync("two@example.com", CancellationToken.None)).IsTrue();
        await Assert.That(await index.GetLastUidAsync("INBOX", 1, CancellationToken.None)).IsEqualTo(0L);
    }
}
