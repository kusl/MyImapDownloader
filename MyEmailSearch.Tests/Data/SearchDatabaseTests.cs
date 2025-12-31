using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

public class SearchDatabaseTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SearchDatabase _database;

    public SearchDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _database = new SearchDatabase(_dbPath, NullLogger<SearchDatabase>.Instance);
    }

    [Test]
    public async Task Initialize_CreatesDatabase()
    {
        await _database.InitializeAsync();

        await Assert.That(File.Exists(_dbPath)).IsTrue();
    }

    [Test]
    public async Task UpsertEmail_InsertsNewEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-1@example.com");
        await _database.UpsertEmailAsync(email);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_UpdatesExistingEmail()
    {
        await _database.InitializeAsync();

        var email1 = CreateTestEmail("test-1@example.com") with { Subject = "Original" };
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-1@example.com") with { Subject = "Updated" };
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task EmailExists_ReturnsTrueForExistingEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-exists@example.com");
        await _database.UpsertEmailAsync(email);

        var exists = await _database.EmailExistsAsync("test-exists@example.com");
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task EmailExists_ReturnsFalseForNonExistingEmail()
    {
        await _database.InitializeAsync();

        var exists = await _database.EmailExistsAsync("nonexistent@example.com");
        await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task Query_ByFromAddress_ReturnsMatchingEmails()
    {
        await _database.InitializeAsync();

        await _database.UpsertEmailAsync(CreateTestEmail("test-1") with { FromAddress = "alice@example.com" });
        await _database.UpsertEmailAsync(CreateTestEmail("test-2") with { FromAddress = "bob@example.com" });
        await _database.UpsertEmailAsync(CreateTestEmail("test-3") with { FromAddress = "alice@example.com" });

        var query = new SearchQuery { FromAddress = "alice@example.com" };
        var results = await _database.QueryAsync(query);

        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IsHealthy_ReturnsTrueForHealthyDatabase()
    {
        await _database.InitializeAsync();

        var healthy = await _database.IsHealthyAsync();

        await Assert.That(healthy).IsTrue();
    }

    private static EmailDocument CreateTestEmail(string messageId) => new()
    {
        MessageId = messageId,
        FilePath = $"/test/{messageId}.eml",
        FromAddress = "sender@example.com",
        Subject = "Test Subject",
        DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();

        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch { /* Ignore cleanup errors */ }
    }
}
