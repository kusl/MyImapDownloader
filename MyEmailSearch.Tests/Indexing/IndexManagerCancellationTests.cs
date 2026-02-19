using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for IndexManager cancellation and progress reporting.
/// </summary>
public class IndexManagerCancellationTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("index_cancel_test");
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

    private async Task CreateEmlFileAsync(string folder, string messageId)
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dir = Path.Combine(archivePath, folder, "cur");
        Directory.CreateDirectory(dir);

        var content = $"Message-ID: <{messageId}>\r\n" +
            $"Subject: Test {messageId}\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n";

        await File.WriteAllTextAsync(Path.Combine(dir, $"{messageId}.eml"), content);
    }

    [Test]
    public async Task IndexAsync_CancellationToken_StopsProcessing()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");

        for (var i = 0; i < 20; i++)
        {
            await CreateEmlFileAsync("INBOX", $"cancel{i}@example.com");
        }

        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.IndexAsync(archivePath, includeContent: false, ct: cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Test]
    public async Task IndexAsync_ReportsProgress()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        await CreateEmlFileAsync("INBOX", "progress1@example.com");
        await CreateEmlFileAsync("INBOX", "progress2@example.com");

        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        var progressReports = new List<IndexingProgress>();
        var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

        await manager.IndexAsync(archivePath, includeContent: false, progress: progress);

        await Task.Delay(200);

        await Assert.That(progressReports.Count).IsGreaterThanOrEqualTo(1);
    }
}
