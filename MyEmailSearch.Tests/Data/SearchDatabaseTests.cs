using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

public class SearchDatabaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("search_db_test");
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
        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task InitializeAsync_CreatesDatabase()
    {
        var db = await CreateDatabaseAsync();
        var dbPath = Path.Combine(_temp.Path, "search.db");
        await Assert.That(File.Exists(dbPath)).IsTrue();
    }

    [Test]
    public async Task UpsertEmailAsync_InsertsNewEmail()
    {
        var db = await CreateDatabaseAsync();
        var doc = CreateEmailDocument("test1@example.com");

        await db.UpsertEmailAsync(doc);
        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmailAsync_UpdatesExistingEmail()
    {
        var db = await CreateDatabaseAsync();
        var doc1 = CreateEmailDocument("update@example.com", subject: "Original");
        var doc2 = CreateEmailDocument("update@example.com", subject: "Updated");

        await db.UpsertEmailAsync(doc1);
        await db.UpsertEmailAsync(doc2);
        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FindsByFullText()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("find1@example.com", subject: "Important Meeting"));
        await db.UpsertEmailAsync(CreateEmailDocument("find2@example.com", subject: "Casual Chat"));

        var results = await db.SearchAsync(new SearchQuery { ContentTerms = "important" });

        await Assert.That(results.Count).IsEqualTo(1);
        results[0].Subject.Should().Contain("Important");
    }

    [Test]
    public async Task SearchAsync_FiltersByFromAddress()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("from1@example.com", from: "alice@example.com"));
        await db.UpsertEmailAsync(CreateEmailDocument("from2@example.com", from: "bob@example.com"));

        var results = await db.SearchAsync(new SearchQuery { FromAddress = "alice@example.com" });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FiltersByToAddress()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("to1@example.com", to: "recipient1@example.com"));
        await db.UpsertEmailAsync(CreateEmailDocument("to2@example.com", to: "recipient2@example.com"));

        var results = await db.SearchAsync(new SearchQuery { ToAddress = "recipient1@example.com" });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FiltersByDateRange()
    {
        var db = await CreateDatabaseAsync();
        var jan = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var mar = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        
        await db.UpsertEmailAsync(CreateEmailDocument("jan@example.com", date: jan));
        await db.UpsertEmailAsync(CreateEmailDocument("mar@example.com", date: mar));

        var results = await db.SearchAsync(new SearchQuery 
        { 
            DateFrom = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero)
        });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_CombinesMultipleFilters()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("combo1@example.com", 
            from: "alice@example.com", subject: "Project Update"));
        await db.UpsertEmailAsync(CreateEmailDocument("combo2@example.com", 
            from: "alice@example.com", subject: "Meeting Notes"));
        await db.UpsertEmailAsync(CreateEmailDocument("combo3@example.com", 
            from: "bob@example.com", subject: "Project Update"));

        var results = await db.SearchAsync(new SearchQuery 
        { 
            FromAddress = "alice@example.com",
            ContentTerms = "project"
        });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsAllFiles()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("file1@example.com", filePath: "/path/to/file1.eml"));
        await db.UpsertEmailAsync(CreateEmailDocument("file2@example.com", filePath: "/path/to/file2.eml"));

        var files = await db.GetKnownFilesAsync();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_ForValidDatabase()
    {
        var db = await CreateDatabaseAsync();
        var healthy = await db.IsHealthyAsync();
        await Assert.That(healthy).IsTrue();
    }

    private static EmailDocument CreateEmailDocument(
        string messageId,
        string? subject = null,
        string? from = null,
        string? to = null,
        string? filePath = null,
        DateTimeOffset? date = null)
    {
        return new EmailDocument
        {
            MessageId = messageId,
            FilePath = filePath ?? $"/test/{messageId}.eml",
            Subject = subject ?? "Test Subject",
            FromAddress = from ?? "sender@example.com",
            ToAddress = to ?? "recipient@example.com",
            DateSent = date ?? DateTimeOffset.UtcNow,
            DateSentUnix = (date ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }
}
