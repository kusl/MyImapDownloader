#!/usr/bin/env bash
set -euo pipefail

echo "Fixing remaining compilation errors..."

# -----------------------------------------------------------------------------
# FIX: Update SearchDatabaseFtsTests.cs to remove references to non-existent methods
# The PrepareFts5ColumnQuery method was proposed but not implemented
# -----------------------------------------------------------------------------

cat > MyEmailSearch.Tests/Data/SearchDatabaseFtsTests.cs << 'EOF'
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
EOF

echo "   ✓ Updated SearchDatabaseFtsTests.cs"

# -----------------------------------------------------------------------------
# FIX: Update SmokeTests.cs to fix the warning about constant value
# -----------------------------------------------------------------------------

cat > MyEmailSearch.Tests/SmokeTests.cs << 'EOF'
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify core types compile and are accessible.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task CoreTypes_AreAccessible()
    {
        // Verify core types can be instantiated
        var parser = new QueryParser();
        var generator = new SnippetGenerator();
        
        await Assert.That(parser).IsNotNull();
        await Assert.That(generator).IsNotNull();
    }

    [Test]
    public async Task QueryParser_CanBeInstantiated()
    {
        var parser = new QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task SnippetGenerator_CanBeInstantiated()
    {
        var generator = new SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }

    [Test]
    public async Task SearchQuery_HasDefaultValues()
    {
        var query = new SearchQuery();
        await Assert.That(query.Take).IsEqualTo(100);
        await Assert.That(query.Skip).IsEqualTo(0);
    }

    [Test]
    public async Task EmailDocument_CanBeCreated()
    {
        var doc = new EmailDocument
        {
            MessageId = "test@example.com",
            FilePath = "/test/path.eml",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await Assert.That(doc.MessageId).IsEqualTo("test@example.com");
    }
}
EOF

echo "   ✓ Updated SmokeTests.cs to fix warning"

# Build and test
echo ""
echo "Building..."
dotnet build --no-restore -c Debug

echo ""
echo "Running tests..."
dotnet test --no-build -c Debug --verbosity normal

echo ""
echo "✓ All fixes applied successfully!"
