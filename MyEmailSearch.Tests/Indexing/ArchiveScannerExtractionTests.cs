using MyEmailSearch.Indexing;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for ArchiveScanner's account/folder extraction from file paths.
/// </summary>
public class ArchiveScannerExtractionTests
{
    [Test]
    public async Task ExtractAccountName_StandardPath_ReturnsAccountFolder()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "work_account", "INBOX", "cur", "email.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsEqualTo("work_account");
    }

    [Test]
    public async Task ExtractAccountName_ShortPath_ReturnsNull()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "email.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsNull();
    }

    [Test]
    public async Task ExtractFolderName_StandardPath_ReturnsFolderName()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "account", "INBOX", "cur", "email.eml");

        var folder = ArchiveScanner.ExtractFolderName(filePath, archivePath);

        await Assert.That(folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task ExtractFolderName_ShortPath_ReturnsNull()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "account", "email.eml");

        var folder = ArchiveScanner.ExtractFolderName(filePath, archivePath);

        await Assert.That(folder).IsNull();
    }

    [Test]
    public async Task ExtractAccountName_SentFolder_ReturnsAccount()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "personal", "Sent", "cur", "msg.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsEqualTo("personal");
    }
}
