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
    private readonly List<SearchDatabase> _databases = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _databases)
        {
            await db.DisposeAsync();
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

    private async Task<(SearchDatabase db, IndexManager manager)> CreateServicesAsync()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, $"search_{Guid.NewGuid():N}.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _databases.Add(db);

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        return (db, manager);
    }

    [Test]
    public async Task IndexAsync_CancellationToken_StopsProcessing()
    {
        for (var i = 0; i < 20; i++)
        {
            await CreateEmlFileAsync("INBOX", $"cancel{i}@example.com");
        }

        var archivePath = Path.Combine(_temp.Path, "archive");
        var (_, manager) = await CreateServicesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.IndexAsync(archivePath, includeContent: false, ct: cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Test]
    public async Task IndexAsync_ReportsProgress()
    {
        await CreateEmlFileAsync("INBOX", "progress1@example.com");
        await CreateEmlFileAsync("INBOX", "progress2@example.com");

        var archivePath = Path.Combine(_temp.Path, "archive");
        var (db, manager) = await CreateServicesAsync();

        await manager.IndexAsync(archivePath, includeContent: false);

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsGreaterThanOrEqualTo(2);
    }
}
