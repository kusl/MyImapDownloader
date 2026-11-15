using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Tests;

public class IndexManagerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        _temp.Dispose();
    }

    private string Archive => Path.Combine(_temp.Path, "archive");

    private async Task<IndexManager> CreateAsync()
    {
        _database = await SearchDatabase.OpenAsync(Path.Combine(_temp.Path, "search.db"));
        return new IndexManager(
            _database,
            new EmailParser(NullLogger<EmailParser>.Instance, TimeProvider.System),
            NullLogger<IndexManager>.Instance,
            TimeProvider.System);
    }

    private sealed class RecordingProgress : IProgress<IndexingProgress>
    {
        public List<IndexingProgress> Reports { get; } = [];

        public void Report(IndexingProgress value) => Reports.Add(value);
    }

    [Test]
    public async Task IndexAsync_IndexesEveryEmailAndRecordsTimestamp()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one");
        Sample.WriteEmail(Archive, "home", "Sent", "two");
        var manager = await CreateAsync();
        var progress = new RecordingProgress();

        var result = await manager.IndexAsync(Archive, includeContent: true, progress);

        await Assert.That(result.Indexed).IsEqualTo(2);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(result.Errors).IsEqualTo(0);
        await Assert.That(await _database!.GetEmailCountAsync()).IsEqualTo(2L);
        await Assert.That(await _database.CountAsync(new SearchQuery { Account = "home", Folder = "Sent" })).IsEqualTo(1);
        await Assert.That(await _database.GetMetadataAsync(SearchDatabase.LastIndexedKey)).IsNotNull();
        await Assert.That(progress.Reports.Count).IsEqualTo(2);
        await Assert.That(progress.Reports[1].Processed).IsEqualTo(2);
        await Assert.That(progress.Reports[1].Percentage).IsEqualTo(100d);
    }

    [Test]
    public async Task IndexAsync_SkipsUnchangedFilesAndReindexesModifiedOnes()
    {
        var unchanged = Sample.WriteEmail(Archive, "work", "INBOX", "one");
        var modified = Sample.WriteEmail(Archive, "work", "INBOX", "two", subject: "Before");
        var manager = await CreateAsync();
        await manager.IndexAsync(Archive, includeContent: false);

        File.WriteAllText(modified, Sample.Email("two@example.com", "After", "Body"));
        File.SetLastWriteTimeUtc(modified, File.GetLastWriteTimeUtc(unchanged).AddMinutes(5));
        Sample.WriteEmail(Archive, "work", "INBOX", "three");
        var result = await manager.IndexAsync(Archive, includeContent: false);

        await Assert.That(result.Indexed).IsEqualTo(2);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(await _database!.GetEmailCountAsync()).IsEqualTo(3L);
        await Assert.That(await _database.CountAsync(new SearchQuery { Subject = "After" })).IsEqualTo(1);
        await Assert.That(await _database.CountAsync(new SearchQuery { Subject = "Before" })).IsEqualTo(0);
    }

    [Test]
    public async Task IndexAsync_ContentIsSearchableOnlyWhenRequested()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one", body: "The kafka cluster is healthy.");
        var manager = await CreateAsync();

        await manager.IndexAsync(Archive, includeContent: false);
        var withoutContent = await _database!.CountAsync(new SearchQuery { ContentTerms = "kafka" });
        await manager.RebuildAsync(Archive, includeContent: true);
        var withContent = await _database.CountAsync(new SearchQuery { ContentTerms = "kafka" });

        await Assert.That(withoutContent).IsEqualTo(0);
        await Assert.That(withContent).IsEqualTo(1);
    }

    [Test]
    public async Task IndexAsync_CountsUnreadableEmailsAsErrorsAndContinues()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "good");
        File.WriteAllText(Path.Combine(Archive, "work", "INBOX", "cur", "empty.eml"), "");
        var manager = await CreateAsync();

        var result = await manager.IndexAsync(Archive, includeContent: false);

        await Assert.That(result.Indexed + result.Errors).IsEqualTo(2);
        await Assert.That(result.Indexed).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task IndexAsync_MissingArchiveIndexesNothing()
    {
        var manager = await CreateAsync();

        var result = await manager.IndexAsync(Path.Combine(_temp.Path, "missing"), includeContent: false);

        await Assert.That(result.Indexed).IsEqualTo(0);
        await Assert.That(result.Errors).IsEqualTo(0);
    }

    [Test]
    public async Task IndexAsync_HandlesMoreThanOneBatch()
    {
        for (var i = 0; i < 230; i++)
        {
            Sample.WriteEmail(Archive, "work", "INBOX", $"message{i}");
        }

        var manager = await CreateAsync();

        var result = await manager.IndexAsync(Archive, includeContent: false);

        await Assert.That(result.Indexed).IsEqualTo(230);
        await Assert.That(await _database!.GetEmailCountAsync()).IsEqualTo(230L);
    }

    [Test]
    public async Task IndexAsync_CancelledTokenStopsBeforeIndexing()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one");
        var manager = await CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await manager.IndexAsync(Archive, includeContent: false, ct: cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Test]
    public async Task RebuildAsync_DropsRowsForFilesThatNoLongerMatch()
    {
        var path = Sample.WriteEmail(Archive, "work", "INBOX", "one");
        var manager = await CreateAsync();
        await manager.IndexAsync(Archive, includeContent: false);
        await _database!.UpsertAsync([Sample.Document("/stale/path.eml")]);

        var result = await manager.RebuildAsync(Archive, includeContent: false);

        var known = await _database.GetKnownFilesAsync();
        await Assert.That(result.Indexed).IsEqualTo(1);
        await Assert.That(known.Count).IsEqualTo(1);
        await Assert.That(known.ContainsKey(path)).IsTrue();
    }
}
