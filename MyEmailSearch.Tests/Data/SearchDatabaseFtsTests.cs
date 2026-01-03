using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for FTS5 full-text search functionality.
/// </summary>
public class SearchDatabaseFtsTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchDatabaseFtsTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"fts_test_{Guid.NewGuid():N}");
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

    private async Task<SearchDatabase> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(_testDirectory, "test.db");
        var logger = new NullLogger<SearchDatabase>();
        var db = new SearchDatabase(dbPath, logger);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmptyString_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test*");
        await Assert.That(result).IsEqualTo("\"test\"*");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithoutWildcard_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test query");
        await Assert.That(result).IsEqualTo("\"test query\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.EscapeFts5Query(null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task EscapeFts5Query_WithQuotes_EscapesThem()
    {
        var result = SearchDatabase.EscapeFts5Query("test \"with\" quotes");
        result.Should().Contain("\"\"");
    }

    [Test]
    public async Task QueryAsync_SubjectSearch_FindsMatchingEmail()
    {
        // Arrange
        var db = await CreateDatabaseAsync();
        
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "test1@example.com",
            FilePath = "/test/email1.eml",
            Subject = "Important Meeting Tomorrow",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "test2@example.com",
            FilePath = "/test/email2.eml",
            Subject = "Lunch Plans",
            FromAddress = "other@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Act - Search by subject using LIKE
        var results = await db.QueryAsync(new SearchQuery
        {
            Subject = "Meeting",
            Take = 100
        });

        // Assert
        await Assert.That(results.Count).IsEqualTo(1);
        results[0].Subject.Should().Contain("Meeting");
    }

    [Test]
    public async Task QueryAsync_ContentSearch_FindsMatchingEmail()
    {
        // Arrange
        var db = await CreateDatabaseAsync();
        
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "combined@example.com",
            FilePath = "/test/combined.eml",
            Subject = "Kafka Discussion",
            BodyText = "Let's discuss the Kafka message broker implementation",
            FromAddress = "dev@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Act - Search body content using FTS5
        var results = await db.QueryAsync(new SearchQuery
        {
            ContentTerms = "broker",
            Take = 100
        });

        // Assert
        await Assert.That(results.Count).IsEqualTo(1);
    }

    private class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
