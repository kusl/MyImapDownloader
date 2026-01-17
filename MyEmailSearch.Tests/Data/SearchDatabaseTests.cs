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
        var email = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_UpdatesExistingFile()
    {
        // Same file path, same message ID -> Should update, count remains 1
        await _database.InitializeAsync();

        var email1 = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email1);

        // Create a new email with same file path but different subject
        var email2 = CreateTestEmail("test-1");
        email2.Subject = "Updated Subject";
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_InsertsMultipleEmails()
    {
        await _database.InitializeAsync();

        var email1 = CreateTestEmail("test-1");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-2");
        await _database.UpsertEmailAsync(email2);

        var email3 = CreateTestEmail("test-3");
        await _database.UpsertEmailAsync(email3);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task Upsert_InsertsMultipleEmails()
    {
        await _database.InitializeAsync();

        var emails = new List<EmailDocument>
        {
            CreateTestEmail("batch-1"),
            CreateTestEmail("batch-2"),
            CreateTestEmail("batch-3")
        };

        await _database.UpsertEmailsAsync(emails);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsFilePaths()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("known-file-test");
        email.LastModifiedTicks = 12345678;
        await _database.UpsertEmailAsync(email);

        var knownFiles = await _database.GetKnownFilesAsync();

        await Assert.That(knownFiles.ContainsKey(email.FilePath)).IsTrue();
        await Assert.That(knownFiles[email.FilePath]).IsEqualTo(12345678);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_WhenDatabaseIsValid()
    {
        await _database.InitializeAsync();

        var isHealthy = await _database.IsHealthyAsync();

        await Assert.That(isHealthy).IsTrue();
    }

    private static EmailDocument CreateTestEmail(string id)
    {
        return new EmailDocument
        {
            MessageId = $"{id}@test.com",
            FilePath = $"/test/emails/{id}.eml",
            FromAddress = "sender@test.com",
            Subject = $"Test Subject {id}",
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
        }
        catch { }
    }
}
