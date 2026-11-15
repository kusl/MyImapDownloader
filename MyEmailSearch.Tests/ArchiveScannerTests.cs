using MyEmailSearch.Indexing;

namespace MyEmailSearch.Tests;

public class ArchiveScannerTests
{
    [Test]
    public async Task FindEmails_ReturnsOnlyEmlFilesRecursively()
    {
        using var temp = new TempDirectory();
        var first = Sample.WriteEmail(temp.Path, "work", "INBOX", "one");
        var second = Sample.WriteEmail(temp.Path, "home", "Sent", "two");
        await File.WriteAllTextAsync(first + ".meta.json", "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "index.v1.db"), "x");

        var found = ArchiveScanner.FindEmails(temp.Path).Order().ToList();

        await Assert.That(found.Count).IsEqualTo(2);
        await Assert.That(found.Contains(first)).IsTrue();
        await Assert.That(found.Contains(second)).IsTrue();
    }

    [Test]
    public async Task FindEmails_MissingArchiveIsEmpty()
    {
        using var temp = new TempDirectory();

        var found = ArchiveScanner.FindEmails(Path.Combine(temp.Path, "missing")).ToList();

        await Assert.That(found.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AccountAndFolder_ComeFromTheRelativePath()
    {
        var archive = Path.Combine(Path.GetTempPath(), "archive");
        var file = Path.Combine(archive, "work", "INBOX", "cur", "one.eml");

        await Assert.That(ArchiveScanner.AccountOf(file, archive)).IsEqualTo("work");
        await Assert.That(ArchiveScanner.FolderOf(file, archive)).IsEqualTo("INBOX");
    }

    [Test]
    public async Task AccountAndFolder_AreNullForShallowPaths()
    {
        var archive = Path.Combine(Path.GetTempPath(), "archive");

        await Assert.That(ArchiveScanner.AccountOf(Path.Combine(archive, "one.eml"), archive)).IsNull();
        await Assert.That(ArchiveScanner.FolderOf(Path.Combine(archive, "work", "one.eml"), archive)).IsNull();
    }
}
