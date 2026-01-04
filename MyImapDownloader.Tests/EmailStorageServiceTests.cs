using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using TUnit.Core;

namespace MyImapDownloader.Tests;

public sealed class EmailStorageServiceTests : IAsyncDisposable
{
    private readonly string _tempRoot;

    public EmailStorageServiceTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "imap-tests-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();

        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static MemoryStream CreateSimpleEmail(
        string messageId,
        string subject = "test",
        string body = "hello")
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
    public async Task SaveStreamAsync_creates_maildir_structure()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<a@test>");

        var saved = await svc.SaveStreamAsync(
            stream,
            "<a@test>",
            DateTimeOffset.UtcNow,
            "Archives/2021",
            CancellationToken.None);

        saved.Should().BeTrue();

        var folder = Path.Combine(_tempRoot, "Archives_2021");
        Directory.Exists(Path.Combine(folder, "cur")).Should().BeTrue();
        Directory.Exists(Path.Combine(folder, "new")).Should().BeTrue();
        Directory.Exists(Path.Combine(folder, "tmp")).Should().BeTrue();
    }

    [Test]
    public async Task SaveStreamAsync_sanitizes_message_id_with_slashes()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        using var stream =
            CreateSimpleEmail("<kushalgmx/playwright/test@github.com>");

        var saved = await svc.SaveStreamAsync(
            stream,
            "<kushalgmx/playwright/test@github.com>",
            DateTimeOffset.UtcNow,
            "Archives/2021",
            CancellationToken.None);

        saved.Should().BeTrue();

        var cur = Path.Combine(_tempRoot, "Archives_2021", "cur");
        var files = Directory.GetFiles(cur, "*.eml");

        files.Should().ContainSingle();

        files[0].Should().NotContain("/");
        files[0].Should().NotContain("\\");
    }

    [Test]
    public async Task SaveStreamAsync_does_not_throw_if_cur_directory_was_deleted()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        var folder = Path.Combine(_tempRoot, "Archives_2021");
        Directory.CreateDirectory(folder);

        var curPath = Path.Combine(folder, "cur");
        if (Directory.Exists(curPath))
            Directory.Delete(curPath, recursive: true);

        using var stream = CreateSimpleEmail("<b@test>");

        Func<Task> act = async () =>
        {
            await svc.SaveStreamAsync(
                stream,
                "<b@test>",
                DateTimeOffset.UtcNow,
                "Archives/2021",
                CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SaveStreamAsync_deduplicates_by_message_id()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        using var s1 = CreateSimpleEmail("<dup@test>");
        using var s2 = CreateSimpleEmail("<dup@test>");

        var first = await svc.SaveStreamAsync(
            s1,
            "<dup@test>",
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        var second = await svc.SaveStreamAsync(
            s2,
            "<dup@test>",
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Test]
    public async Task SaveStreamAsync_writes_meta_json_sidecar()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<meta@test>");

        await svc.SaveStreamAsync(
            stream,
            "<meta@test>",
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        var cur = Path.Combine(_tempRoot, "Inbox", "cur");
        var metaFiles = Directory.GetFiles(cur, "*.meta.json");

        metaFiles.Should().ContainSingle();

        var json = await File.ReadAllTextAsync(metaFiles[0]);
        json.Should().Contain("\"MessageId\"");
        json.Should().Contain("\"Folder\"");
    }

    [Test]
    public async Task GetLastUidAsync_resets_when_uidvalidity_changes()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        await svc.UpdateLastUidAsync(
            "Inbox",
            lastUid: 123,
            validity: 1,
            CancellationToken.None);

        var sameValidity = await svc.GetLastUidAsync(
            "Inbox",
            currentValidity: 1,
            CancellationToken.None);

        var changedValidity = await svc.GetLastUidAsync(
            "Inbox",
            currentValidity: 999,
            CancellationToken.None);

        sameValidity.Should().Be(123);
        changedValidity.Should().Be(0);
    }

    [Test]
    public async Task UpdateLastUidAsync_does_not_move_cursor_backwards()
    {
        var svc = new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _tempRoot);

        await svc.InitializeAsync(CancellationToken.None);

        await svc.UpdateLastUidAsync("Inbox", 100, 1, CancellationToken.None);
        await svc.UpdateLastUidAsync("Inbox", 50, 1, CancellationToken.None);

        var uid = await svc.GetLastUidAsync(
            "Inbox",
            currentValidity: 1,
            CancellationToken.None);

        uid.Should().Be(100);
    }
}
