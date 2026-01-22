using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MimeKit;

namespace MyImapDownloader.Tests.Services;

public class EmailStorageServiceTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("storage_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private EmailStorageService CreateService()
    {
        return new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _temp.Path);
    }

    private static MemoryStream CreateSimpleEmail(
        string messageId,
        string subject = "Test",
        string body = "Hello")
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@test.com"));
        msg.To.Add(new MailboxAddress("Receiver", "receiver@test.com"));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };

        var ms = new MemoryStream();
        msg.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }

    [Test]
    public async Task InitializeAsync_CreatesDatabase()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var dbPath = Path.Combine(_temp.Path, "index.v1.db");
        await Assert.That(File.Exists(dbPath)).IsTrue();
    }

    [Test]
    public async Task SaveStreamAsync_CreatesMaildirStructure()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<test1@example.com>");
        var saved = await service.SaveStreamAsync(
            stream,
            "<test1@example.com>",
            DateTimeOffset.UtcNow,
            "INBOX",
            CancellationToken.None);

        await Assert.That(saved).IsTrue();

        var inboxPath = Path.Combine(_temp.Path, "INBOX");
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "cur"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "new"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "tmp"))).IsTrue();
    }

    [Test]
    public async Task SaveStreamAsync_DeduplicatesByMessageId()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream1 = CreateSimpleEmail("<dup@test.com>");
        using var stream2 = CreateSimpleEmail("<dup@test.com>");

        var first = await service.SaveStreamAsync(
            stream1, "<dup@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);
        var second = await service.SaveStreamAsync(
            stream2, "<dup@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
    }

    [Test]
    public async Task SaveStreamAsync_CreatesSidecarMetadata()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<meta@test.com>", "Test Subject");
        await service.SaveStreamAsync(
            stream, "<meta@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        var curPath = Path.Combine(_temp.Path, "INBOX", "cur");
        var metaFiles = Directory.GetFiles(curPath, "*.meta.json");

        await Assert.That(metaFiles.Length).IsEqualTo(1);

        var content = await File.ReadAllTextAsync(metaFiles[0]);
        content.Should().Contain("Test Subject");
    }

    [Test]
    public async Task SaveStreamAsync_SanitizesMessageIdWithSlashes()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<user/repo/test@github.com>");
        var saved = await service.SaveStreamAsync(
            stream, "<user/repo/test@github.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        await Assert.That(saved).IsTrue();

        var curPath = Path.Combine(_temp.Path, "INBOX", "cur");
        var files = Directory.GetFiles(curPath, "*.eml");
        await Assert.That(files.Length).IsEqualTo(1);

        var fileName = Path.GetFileName(files[0]);
        fileName.Should().NotContain("/");
        fileName.Should().NotContain("\\");
    }

    [Test]
    public async Task GetLastUidAsync_ReturnsZero_WhenNoSyncState()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var lastUid = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);

        await Assert.That(lastUid).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateLastUidAsync_PersistsUid()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.UpdateLastUidAsync("INBOX", 100, 12345, CancellationToken.None);
        var lastUid = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);

        await Assert.That(lastUid).IsEqualTo(100);
    }

    [Test]
    public async Task GetLastUidAsync_ResetsOnUidValidityChange()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.UpdateLastUidAsync("INBOX", 100, 12345, CancellationToken.None);
        var sameValidity = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);
        var changedValidity = await service.GetLastUidAsync("INBOX", 99999, CancellationToken.None);

        await Assert.That(sameValidity).IsEqualTo(100);
        await Assert.That(changedValidity).IsEqualTo(0);
    }

    [Test]
    public async Task NormalizeMessageId_RemovesInvalidCharacters()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<test/path:id@example.com>");

        normalized.Should().NotContain("/");
        normalized.Should().NotContain(":");
        normalized.Should().NotContain("<");
        normalized.Should().NotContain(">");
    }

    [Test]
    public async Task ComputeHash_ReturnsConsistentHash()
    {
        var hash1 = EmailStorageService.ComputeHash("test input");
        var hash2 = EmailStorageService.ComputeHash("test input");
        var hash3 = EmailStorageService.ComputeHash("different input");

        await Assert.That(hash1).IsEqualTo(hash2);
        await Assert.That(hash1).IsNotEqualTo(hash3);
    }
}
