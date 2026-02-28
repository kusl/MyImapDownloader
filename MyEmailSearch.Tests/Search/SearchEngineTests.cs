using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Search;

public class SearchEngineTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("search_engine_test");
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

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_temp.Path, "test.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var queryParser = new QueryParser();
        var engine = new SearchEngine(db, queryParser,
            NullLogger<SearchEngine>.Instance);

        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_ReturnsResults()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("search@example.com", "Test Subject"));

        var results = await engine.SearchAsync("test");

        await Assert.That(results.TotalCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_AppliesPagination()
    {
        var (db, engine) = await CreateServicesAsync();
        for (int i = 0; i < 15; i++)
        {
            await db.UpsertEmailAsync(CreateDocument($"page{i}@example.com", $"Page Test {i}"));
        }

        var results = await engine.SearchAsync("page", limit: 5, offset: 0);

        await Assert.That(results.Results.Count).IsEqualTo(5);
        await Assert.That(results.TotalCount).IsEqualTo(15);
        await Assert.That(results.HasMore).IsTrue();
    }

    [Test]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("empty@example.com", "Subject"));

        var results = await engine.SearchAsync("");

        await Assert.That(results.TotalCount).IsEqualTo(0);
    }

    [Test]
    public async Task SearchAsync_ReturnsQueryTime()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("time@example.com", "Subject"));

        var results = await engine.SearchAsync("subject");

        await Assert.That(results.QueryTime.TotalMilliseconds).IsGreaterThanOrEqualTo(0);
    }

    private static EmailDocument CreateDocument(string messageId, string subject)
    {
        return new EmailDocument
        {
            MessageId = messageId,
            FilePath = $"/test/{messageId}.eml",
            Subject = subject,
            FromAddress = "sender@example.com",
            ToAddressesJson = "[\"recipient@example.com\"]",
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }
}
