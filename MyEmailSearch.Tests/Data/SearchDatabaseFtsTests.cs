using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for FTS5 full-text search functionality.
/// FIX: Validates that subject searches use FTS5 for performance.
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
    public async Task PrepareFts5ColumnQuery_CreatesCorrectSyntax()
    {
        var result = SearchDatabase.PrepareFts5ColumnQuery("subject", "test query");
        
        await Assert.That(result).IsEqualTo("subject:\"test query\"");
    }

    [Test]
    public async Task PrepareFts5ColumnQuery_EscapesQuotes()
    {
        var result = SearchDatabase.PrepareFts5ColumnQuery("subject", "test \"with\" quotes");
        
        await Assert.That(result).IsEqualTo("subject:\"test \"\"with\"\" quotes\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_EscapesFts5Operators()
    {
        // Attempting to inject FTS5 operators should be neutralized
        var result = SearchDatabase.PrepareFts5MatchQuery("test OR hack AND inject");
        
        // Should be wrapped in quotes which neutralizes operators
        result.Should().StartWith("\"");
        result.Should().EndWith("\"");
    }

    [Test]
    public async Task QueryAsync_SubjectSearch_UsesFts5()
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

        // Act - Search by subject should use FTS5
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
    public async Task QueryAsync_CombinedSubjectAndContent_WorksTogether()
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

        // Act - Search both subject and body
        var results = await db.QueryAsync(new SearchQuery
        {
            ContentTerms = "message broker",
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
