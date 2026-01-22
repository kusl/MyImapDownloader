using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using MyImapDownloader;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public class EmailStorageSanitizationTests
{
    [Test]
    public async Task NormalizeMessageId_RemovesDirectorySeparators()
    {
        var raw = "playwright-test/check/suites/<id>@github.com";

        var normalized = EmailStorageService.NormalizeMessageId(raw);

        await Assert.That(normalized).DoesNotContain("/");
        await Assert.That(normalized).DoesNotContain("\\");
    }


    [Test]
    public async Task GenerateFilename_IsSingleFileName()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var safeId = "abc_def_ghi";

        var name = EmailStorageService.GenerateFilename(date, safeId);

        await Assert.That(Path.GetFileName(name)).IsEqualTo(name);
        await Assert.That(name).DoesNotContain("/");
    }

    [Test]
    public async Task SaveStreamAsync_DoesNotCreateDirectoriesFromMessageId()
    {
        using var tempDir = new TempDirectory();
        var logger = TestLogger.Create<EmailStorageService>();

        var service = new EmailStorageService(logger, tempDir.Path);
        await service.InitializeAsync(CancellationToken.None);

        var messageId = "foo/bar/baz@github.com";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Subject: test\r\n\r\nbody"));

        await service.SaveStreamAsync(
            stream,
            messageId,
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        var cur = Path.Combine(tempDir.Path, "Inbox", "cur");

        // No subdirectories under cur
        await Assert.That(Directory.GetDirectories(cur)).IsEmpty();
    }

    [Test]
    public async Task SaveStreamAsync_DuplicateMessage_ReturnsFalse()
    {
        using var tempDir = new TempDirectory();
        var logger = TestLogger.Create<EmailStorageService>();

        var service = new EmailStorageService(logger, tempDir.Path);
        await service.InitializeAsync(CancellationToken.None);

        var msg = "dup@test";
        var content = System.Text.Encoding.UTF8.GetBytes("Subject: dup\r\n\r\nbody");

        await Assert.That(await service.SaveStreamAsync(
            new MemoryStream(content), msg, DateTimeOffset.UtcNow, "Inbox", CancellationToken.None))
            .IsTrue();

        await Assert.That(await service.SaveStreamAsync(
            new MemoryStream(content), msg, DateTimeOffset.UtcNow, "Inbox", CancellationToken.None))
            .IsFalse();
    }


}
