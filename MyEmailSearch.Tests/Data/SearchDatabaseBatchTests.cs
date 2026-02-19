using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase batch operations.
/// </summary>
public class SearchDatabaseBatchTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("db_batch_test");
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task<SearchDatabase> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(_temp.Path, $"test_{Guid.NewGuid():N}.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_InsertsMultipleEmails()
    {
        var db = await CreateDatabaseAsync();

        var docs = Enumerable.Range(0, 50).Select(i => new EmailDocument
        {
            MessageId = $"batch{i}@example.com",
            FilePath = $"/test/batch{i}.eml",
            Subject = $"Batch Email {i}",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }).ToList();

        await db.BatchUpsertEmailsAsync(docs);

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(50);
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_EmptyList_DoesNotThrow()
    {
        var db = await CreateDatabaseAsync();

        await db.BatchUpsertEmailsAsync(new List<EmailDocument>());

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_AllSearchable_AfterInsert()
    {
        var db = await CreateDatabaseAsync();

        var docs = Enumerable.Range(0, 10).Select(i => new EmailDocument
        {
            MessageId = $"searchable{i}@example.com",
            FilePath = $"/test/searchable{i}.eml",
            Subject = $"Searchable BatchItem {i}",
            FromAddress = "batchsender@example.com",
            BodyText = $"Unique content for batch item number {i} with keyword xylophone",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }).ToList();

        await db.BatchUpsertEmailsAsync(docs);

        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "xylophone" });
        await Assert.That(results.Count).IsEqualTo(10);

        var fromResults = await db.QueryAsync(new SearchQuery { FromAddress = "batchsender@example.com" });
        await Assert.That(fromResults.Count).IsEqualTo(10);
    }
}
