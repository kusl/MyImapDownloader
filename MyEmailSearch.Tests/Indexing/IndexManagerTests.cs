using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class IndexManagerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("index_mgr_test");
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

    private async Task<string> CreateEmlFileAsync(string folder, string messageId, string subject)
    {
        var curDir = Path.Combine(_temp.Path, "archive", folder, "cur");
        Directory.CreateDirectory(curDir);
        
        var content = $"""
            Message-ID: <{messageId}>
            Subject: {subject}
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Email body for {subject}
            """;
        
        var path = Path.Combine(curDir, $"{messageId.Replace("@", "_")}.eml");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Test]
    public async Task IndexAsync_IndexesNewEmails()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "test1@example.com", "First Email");
        await CreateEmlFileAsync("INBOX", "test2@example.com", "Second Email");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        var result = await manager.IndexAsync(archivePath, includeContent: true);

        await Assert.That(result.Indexed).IsEqualTo(2);
        await Assert.That(result.Errors).IsEqualTo(0);
    }

    [Test]
    public async Task IndexAsync_SkipsAlreadyIndexedFiles()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "existing@example.com", "Existing Email");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        // Index twice
        var result1 = await manager.IndexAsync(archivePath, includeContent: true);
        var result2 = await manager.IndexAsync(archivePath, includeContent: true);

        await Assert.That(result1.Indexed).IsEqualTo(1);
        await Assert.That(result2.Indexed).IsEqualTo(0);
        await Assert.That(result2.Skipped).IsEqualTo(1);
    }

    [Test]
    public async Task RebuildIndexAsync_ReindexesAllEmails()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "rebuild@example.com", "Rebuild Test");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        // Index first
        await manager.IndexAsync(archivePath, includeContent: true);
        
        // Rebuild
        var result = await manager.RebuildIndexAsync(archivePath, includeContent: true);

        await Assert.That(result.Indexed).IsEqualTo(1);
    }
}
