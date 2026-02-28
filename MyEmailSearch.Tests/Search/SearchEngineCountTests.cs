using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Tests for SearchEngine total count behavior.
/// </summary>
public class SearchEngineCountTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchEngineCountTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"engine_count_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_testDirectory, "test.db");
        var dbLogger = new NullLogger<SearchDatabase>();
        var db = new SearchDatabase(dbPath, dbLogger);
        await db.InitializeAsync();
        _database = db;

        var queryParser = new QueryParser();
        var engineLogger = new NullLogger<SearchEngine>();
        var engine = new SearchEngine(db, queryParser, engineLogger);

        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_WithLimit_ReturnsTotalCountOfAllMatches()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        // Insert 150 emails to "recipient@tilde.team"
        for (var i = 0; i < 150; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                ToAddressesJson = "[\"recipient@tilde.team\"]",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act
        var results = await engine.SearchAsync("to:recipient@tilde.team", limit: 20, offset: 0);

        // Assert - TotalCount should be 150, not capped at 20
        results.TotalCount.Should().Be(150);
        results.Results.Should().HaveCount(20);
        results.HasMore.Should().BeTrue();
    }

    [Test]
    public async Task SearchAsync_Pagination_TotalCountConsistentAcrossPages()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        for (var i = 0; i < 100; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "alice@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act - Get first page
        var page1 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 0);
        // Act - Get second page
        var page2 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 20);
        // Act - Get third page
        var page3 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 40);

        // Assert - Total count should be consistent across pages
        page1.TotalCount.Should().Be(100);
        page2.TotalCount.Should().Be(100);
        page3.TotalCount.Should().Be(100);

        // Results should be paginated correctly
        page1.Results.Should().HaveCount(20);
        page2.Results.Should().HaveCount(20);
        page3.Results.Should().HaveCount(20);
    }
}
