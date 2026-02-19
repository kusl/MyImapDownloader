using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase metadata, size, health, and lifecycle operations.
/// </summary>
public class SearchDatabaseMetadataTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("db_meta_test");
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
    public async Task SetMetadataAsync_And_GetMetadataAsync_RoundTrips()
    {
        var db = await CreateDatabaseAsync();

        await db.SetMetadataAsync("test_key", "test_value");
        var result = await db.GetMetadataAsync("test_key");

        await Assert.That(result).IsEqualTo("test_value");
    }

    [Test]
    public async Task GetMetadataAsync_NonExistentKey_ReturnsNull()
    {
        var db = await CreateDatabaseAsync();

        var result = await db.GetMetadataAsync("nonexistent_key");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SetMetadataAsync_OverwritesExistingKey()
    {
        var db = await CreateDatabaseAsync();

        await db.SetMetadataAsync("version", "1.0");
        await db.SetMetadataAsync("version", "2.0");
        var result = await db.GetMetadataAsync("version");

        await Assert.That(result).IsEqualTo("2.0");
    }

    [Test]
    public async Task GetDatabaseSize_ReturnsPositiveValue()
    {
        var db = await CreateDatabaseAsync();

        // Insert some data to ensure the file has content
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "size@example.com",
            FilePath = "/test/size.eml",
            Subject = "Size Test",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var size = db.GetDatabaseSize();

        await Assert.That(size).IsGreaterThan(0);
    }

    [Test]
    public async Task IsHealthyAsync_OnGoodDatabase_ReturnsTrue()
    {
        var db = await CreateDatabaseAsync();

        var healthy = await db.IsHealthyAsync();

        await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task GetEmailCountAsync_EmptyDatabase_ReturnsZero()
    {
        var db = await CreateDatabaseAsync();

        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetEmailCountAsync_AfterInserts_ReturnsCorrectCount()
    {
        var db = await CreateDatabaseAsync();

        for (var i = 0; i < 5; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"count{i}@example.com",
                FilePath = $"/test/count{i}.eml",
                Subject = $"Count Test {i}",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsInsertedFiles()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "known@example.com",
            FilePath = "/test/known.eml",
            Subject = "Known File",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = 12345
        });

        var knownFiles = await db.GetKnownFilesAsync();

        knownFiles.Should().ContainKey("/test/known.eml");
        await Assert.That(knownFiles["/test/known.eml"]).IsEqualTo(12345);
    }

    [Test]
    public async Task RebuildAsync_ClearsAllData()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "rebuild@example.com",
            FilePath = "/test/rebuild.eml",
            Subject = "Rebuild Test",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var countBefore = await db.GetEmailCountAsync();
        await Assert.That(countBefore).IsEqualTo(1);

        await db.RebuildAsync();

        var countAfter = await db.GetEmailCountAsync();
        await Assert.That(countAfter).IsEqualTo(0);
    }

    [Test]
    public async Task UpsertEmailAsync_UpdatesExistingByFilePath()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "upsert@example.com",
            FilePath = "/test/upsert.eml",
            Subject = "Original Subject",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "upsert@example.com",
            FilePath = "/test/upsert.eml",
            Subject = "Updated Subject",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);

        // Verify the FTS index still works after the update
        var results = await db.QueryAsync(new SearchQuery { Subject = "Updated" });
        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FtsTrigger_AfterUpdate_ReflectsNewContent()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "trigger@example.com",
            FilePath = "/test/trigger.eml",
            Subject = "Alpha",
            BodyText = "Original body text about apples",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Search for original content
        var beforeResults = await db.QueryAsync(new SearchQuery { ContentTerms = "apples" });
        await Assert.That(beforeResults.Count).IsEqualTo(1);

        // Update with new content
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "trigger@example.com",
            FilePath = "/test/trigger.eml",
            Subject = "Beta",
            BodyText = "Updated body text about oranges",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Old content should no longer match
        var oldResults = await db.QueryAsync(new SearchQuery { ContentTerms = "apples" });
        await Assert.That(oldResults.Count).IsEqualTo(0);

        // New content should match
        var newResults = await db.QueryAsync(new SearchQuery { ContentTerms = "oranges" });
        await Assert.That(newResults.Count).IsEqualTo(1);
    }
}
