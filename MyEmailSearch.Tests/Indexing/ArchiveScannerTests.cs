using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class ArchiveScannerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("scanner_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    public async Task ScanForEmails_FindsEmlFiles()
    {
        // Create test .eml files
        var curDir = Path.Combine(_temp.Path, "INBOX", "cur");
        Directory.CreateDirectory(curDir);
        await File.WriteAllTextAsync(Path.Combine(curDir, "test1.eml"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test2.eml"), "Content 2");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScanForEmails_RecursivelySearchesSubfolders()
    {
        var inbox = Path.Combine(_temp.Path, "INBOX", "cur");
        var sent = Path.Combine(_temp.Path, "Sent", "cur");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(sent);
        await File.WriteAllTextAsync(Path.Combine(inbox, "inbox.eml"), "Inbox");
        await File.WriteAllTextAsync(Path.Combine(sent, "sent.eml"), "Sent");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScanForEmails_IgnoresNonEmlFiles()
    {
        var curDir = Path.Combine(_temp.Path, "INBOX", "cur");
        Directory.CreateDirectory(curDir);
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.eml"), "Email");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.meta.json"), "Metadata");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.txt"), "Text");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0]).EndsWith(".eml");
    }

    [Test]
    public async Task ScanForEmails_ReturnsEmptyForEmptyDirectory()
    {
        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(0);
    }
}
