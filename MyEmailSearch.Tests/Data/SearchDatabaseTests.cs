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
        var email1 = CreateTestEmail("test-1", "Subject 1", "/path/to/file1.eml");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-1", "Subject Updated", "/path/to/file1.eml");
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
        
        var files = await _database.GetKnownFilesAsync();
        await Assert.That(files.ContainsKey("/path/to/file1.eml")).IsTrue();
    }

    [Test]
    public async Task UpsertEmail_AllowsDuplicateMessageIds_DifferentFiles()
    {
        // Different file paths, same message ID -> Should insert both, count becomes 2
        await _database.InitializeAsync();
        var email1 = CreateTestEmail("duplicate-id", "Copy 1", "/path/to/inbox/mail.eml");
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("duplicate-id", "Copy 2", "/path/to/trash/mail.eml");
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(2);
        
        // Ensure both files are tracked
        var files = await _database.GetKnownFilesAsync();
        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EmailExists_ReturnsTrueForExistingEmail()
    {
        await _database.InitializeAsync();
        var email = CreateTestEmail("test-exists");
        await _database.UpsertEmailAsync(email);

        var exists = await _database.EmailExistsAsync("test-exists");
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task Query_ByFromAddress_ReturnsMatchingEmails()
    {
        await _database.InitializeAsync();
        await _database.UpsertEmailAsync(CreateTestEmail("1", "S1", "/f1", "alice@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("2", "S2", "/f2", "bob@example.com"));
        await _database.UpsertEmailAsync(CreateTestEmail("3", "S3", "/f3", "alice@example.com"));

        var query = new SearchQuery { FromAddress = "alice@example.com" };
        var results = await _database.QueryAsync(query);

        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsInsertedPaths()
    {
        await _database.InitializeAsync();
        var email = CreateTestEmail("file-test", "Sub", "/unique/path.eml");
        await _database.UpsertEmailAsync(email);

        var knownFiles = await _database.GetKnownFilesAsync();
        
        await Assert.That(knownFiles).ContainsKey(email.FilePath);
        await Assert.That(knownFiles[email.FilePath]).IsEqualTo(email.LastModifiedTicks);
    }

    private static EmailDocument CreateTestEmail(
        string messageId,
        string subject = "Test Subject",
        string filePath = null,
        string fromAddress = "sender@example.com") => new()
        {
            MessageId = messageId,
            FilePath = filePath ?? $"/test/{messageId}.eml",
            FromAddress = fromAddress,
            Subject = subject,
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
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
