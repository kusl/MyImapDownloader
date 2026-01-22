using System;
using System.IO;
using System.Threading.Tasks;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using TUnit.Core;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase total count functionality.
/// </summary>
public class SearchDatabaseCountTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchDatabaseCountTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"count_test_{Guid.NewGuid():N}");
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
    public async Task GetTotalCountForQueryAsync_ReturnsAllMatchingEmails_NotJustLimit()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 150 emails to the same recipient
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

        var query = new SearchQuery
        {
            ToAddress = "recipient@tilde.team",
            Take = 100,  // Limit to 100
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert - should be 150, not 100
        totalCount.Should().Be(150);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_WithFromFilter_ReturnsCorrectCount()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 50 emails from alice
        for (var i = 0; i < 50; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"alice{i}@example.com",
                FilePath = $"/test/alice{i}.eml",
                Subject = $"From Alice {i}",
                FromAddress = "alice@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Insert 30 emails from bob
        for (var i = 0; i < 30; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"bob{i}@example.com",
                FilePath = $"/test/bob{i}.eml",
                Subject = $"From Bob {i}",
                FromAddress = "bob@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            FromAddress = "alice@example.com",
            Take = 10,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        totalCount.Should().Be(50);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_WithDateRange_ReturnsCorrectCount()
    {
        // Arrange
        var db = await CreateDatabaseAsync();
        var baseDate = DateTimeOffset.UtcNow;

        // Insert 20 emails from this week
        for (var i = 0; i < 20; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"recent{i}@example.com",
                FilePath = $"/test/recent{i}.eml",
                Subject = $"Recent Email {i}",
                FromAddress = "sender@example.com",
                DateSentUnix = baseDate.AddDays(-i).ToUnixTimeSeconds(),
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Insert 30 emails from last month
        for (var i = 0; i < 30; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"old{i}@example.com",
                FilePath = $"/test/old{i}.eml",
                Subject = $"Old Email {i}",
                FromAddress = "sender@example.com",
                DateSentUnix = baseDate.AddDays(-30 - i).ToUnixTimeSeconds(),
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            DateFrom = baseDate.AddDays(-7),  // Last 7 days
            Take = 5,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert - should be 8 (days 0-7 inclusive)
        totalCount.Should().Be(8);
    }

    [Test]
    public async Task GetTotalCountForQueryAsync_NoMatches_ReturnsZero()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "test@example.com",
            FilePath = "/test/email.eml",
            Subject = "Test Email",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var query = new SearchQuery
        {
            FromAddress = "nonexistent@example.com",
            Take = 100,
            Skip = 0
        };

        // Act
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        totalCount.Should().Be(0);
    }

    [Test]
    public async Task QueryAsync_ReturnsLimitedResults_WhileCountReturnsAll()
    {
        // Arrange
        var db = await CreateDatabaseAsync();

        // Insert 200 emails
        for (var i = 0; i < 200; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var query = new SearchQuery
        {
            FromAddress = "sender@example.com",
            Take = 50,
            Skip = 0
        };

        // Act
        var results = await db.QueryAsync(query);
        var totalCount = await db.GetTotalCountForQueryAsync(query);

        // Assert
        results.Should().HaveCount(50);      // Limited results
        totalCount.Should().Be(200);         // Actual total
    }
}
