using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

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
        var logger = NullLogger<SearchDatabase>.Instance;
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
        // Should escape internal quotes by doubling them
        result.Should().Contain("\"\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNormalText_WrapsInQuotes()
    {
        var result = SearchDatabase.EscapeFts5Query("hello world");
        await Assert.That(result).IsEqualTo("\"hello world\"");
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
            Subject = "Casual Chat",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Act - search for "important" in subject
        var results = await db.QueryAsync(new SearchQuery { Subject = "Important" });

        // Assert
        await Assert.That(results.Count).IsEqualTo(1);
        results[0].Subject.Should().Contain("Important");
    }

    [Test]
    public async Task QueryAsync_FtsSearch_FindsMatchingEmailByBodyText()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "body1@example.com",
            FilePath = "/test/body1.eml",
            Subject = "Regular Email",
            FromAddress = "sender@example.com",
            BodyText = "This email contains the word kafka which is unique",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "body2@example.com",
            FilePath = "/test/body2.eml",
            Subject = "Another Email",
            FromAddress = "sender@example.com",
            BodyText = "This is just a normal email about nothing special",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Act - search for "kafka" in body
        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "kafka" });

        // Assert
        await Assert.That(results.Count).IsEqualTo(1);
        results[0].MessageId.Should().Be("body1@example.com");
    }
}
