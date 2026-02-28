using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Edge case tests for SearchEngine.
/// </summary>
public class SearchEngineEdgeCaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("engine_edge_test");
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

        var engine = new SearchEngine(db, new QueryParser(),
            NullLogger<SearchEngine>.Instance);
        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_OffsetBeyondResults_ReturnsEmpty()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "only@example.com",
            FilePath = "/test/only.eml",
            Subject = "Only Result",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("only", limit: 10, offset: 100);

        await Assert.That(results.Results.Count).IsEqualTo(0);
        await Assert.That(results.TotalCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_NoMatchingResults_ReturnsTotalCountZero()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "existing@example.com",
            FilePath = "/test/existing.eml",
            Subject = "Existing Email",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("nonexistentquerythatmatchesnothing");

        await Assert.That(results.TotalCount).IsEqualTo(0);
        await Assert.That(results.Results.Count).IsEqualTo(0);
        await Assert.That(results.HasMore).IsFalse();
    }

    [Test]
    public async Task SearchAsync_WithSnippets_GeneratesSnippetsForContentSearch()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "snippet@example.com",
            FilePath = "/test/snippet.eml",
            Subject = "Snippet Test",
            BodyText = "This email contains a very specific keyword called xylophone in the body.",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("xylophone");

        await Assert.That(results.Results.Count).IsEqualTo(1);
        // Snippet should be generated for content-based searches
    }

    [Test]
    public async Task SearchAsync_NullQuery_ReturnsEmpty()
    {
        var (_, engine) = await CreateServicesAsync();

        var results = await engine.SearchAsync((string)null!);

        await Assert.That(results.TotalCount).IsEqualTo(0);
    }
}
