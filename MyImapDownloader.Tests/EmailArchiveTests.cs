using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using MyImapDownloader.Archive;

namespace MyImapDownloader.Tests;

public class EmailArchiveTests : IDisposable
{
    private static readonly DateTimeOffset InternalDate = new(2024, 12, 24, 10, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    private string Current => Path.Combine(_temp.Path, "INBOX", "cur");

    private Task<EmailArchive> OpenAsync() =>
        EmailArchive.OpenAsync(_temp.Path, NullLoggerFactory.Instance, _time, "testhost", CancellationToken.None);

    private static Task<StoreOutcome> StoreAsync(EmailArchive archive, byte[] content, string? messageId) =>
        archive.StoreAsync(new MemoryStream(content), messageId, InternalDate, "INBOX", CancellationToken.None);

    private string[] Emails() => Directory.GetFiles(Current, "*.eml");

    private void DeleteIndex()
    {
        foreach (var file in Directory.GetFiles(_temp.Path, ArchiveIndex.FileName + "*"))
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task StoreAsync_WritesMessageIntoMaildirLayout()
    {
        var content = Eml.Create("abc@example.com");
        await using var archive = await OpenAsync();

        var outcome = await StoreAsync(archive, content, "abc@example.com");

        var expected = Path.Combine(Current, $"{InternalDate.ToUnixTimeSeconds()}.abc@example.com.testhost.eml");
        await Assert.That(outcome is Stored).IsTrue();
        await Assert.That(File.Exists(expected)).IsTrue();
        await Assert.That(Convert.ToBase64String(await File.ReadAllBytesAsync(expected)))
            .IsEqualTo(Convert.ToBase64String(content));
        await Assert.That(Directory.Exists(Path.Combine(_temp.Path, "INBOX", "new"))).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "tmp")).Length).IsEqualTo(0);
    }

    [Test]
    public async Task StoreAsync_WritesSidecarInEstablishedFormat()
    {
        await using var archive = await OpenAsync();

        await StoreAsync(archive, Eml.Create("abc@example.com", "Project Update"), "abc@example.com");

        var sidecar = await File.ReadAllTextAsync(Emails().Single() + ".meta.json");
        await Assert.That(sidecar.ReplaceLineEndings(Environment.NewLine)).IsEqualTo("""
            {
              "MessageId": "abc@example.com",
              "Subject": "Project Update",
              "From": "Sender \u003Csender@example.com\u003E",
              "To": "Receiver \u003Creceiver@example.com\u003E",
              "Date": "2024-12-24T10:30:00Z",
              "Folder": "INBOX",
              "ArchivedAt": "2025-01-02T03:04:05Z",
              "HasAttachments": false
            }
            """);
    }

    [Test]
    public async Task StoreAsync_SanitizesFolderNames()
    {
        await using var archive = await OpenAsync();

        await archive.StoreAsync(
            new MemoryStream(Eml.Create("abc@example.com")),
            "abc@example.com",
            InternalDate,
            "INBOX/Sub Folder",
            CancellationToken.None);

        await Assert.That(Directory.GetFiles(Path.Combine(_temp.Path, "INBOX_Sub_Folder", "cur"), "*.eml").Length)
            .IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_SameMessageTwice_KeepsOneFile()
    {
        await using var archive = await OpenAsync();

        var first = await StoreAsync(archive, Eml.Create("abc@example.com"), "abc@example.com");
        var second = await StoreAsync(archive, Eml.Create("abc@example.com", body: "Different"), "abc@example.com");

        await Assert.That(first is Stored).IsTrue();
        await Assert.That(second is Duplicate).IsTrue();
        await Assert.That(Emails().Length).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_WithoutEnvelopeId_UsesMessageIdHeader()
    {
        await using var archive = await OpenAsync();

        await StoreAsync(archive, Eml.Create("Header@Example.com"), messageId: null);
        var again = await StoreAsync(archive, Eml.Create("header@example.com"), messageId: null);

        await Assert.That(Path.GetFileName(Emails().Single()))
            .IsEqualTo($"{InternalDate.ToUnixTimeSeconds()}._header@example.com_.testhost.eml");
        await Assert.That(again is Duplicate).IsTrue();
    }

    [Test]
    public async Task StoreAsync_WithoutAnyMessageId_UsesContentHash()
    {
        var content = Eml.Create(messageId: null);
        await using var archive = await OpenAsync();

        var first = await StoreAsync(archive, content, messageId: null);
        var second = await StoreAsync(archive, content, messageId: "  ");

        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        await Assert.That(first is Stored).IsTrue();
        await Assert.That(second is Duplicate).IsTrue();
        await Assert.That(Path.GetFileName(Emails().Single()))
            .IsEqualTo($"{InternalDate.ToUnixTimeSeconds()}.{hash}.testhost.eml");
    }

    [Test]
    public async Task StoreAsync_AfterIndexLoss_AdoptsExistingFileAndRestoresSidecar()
    {
        var content = Eml.Create("abc@example.com");
        await using (var archive = await OpenAsync())
        {
            await StoreAsync(archive, content, "abc@example.com");
        }

        DeleteIndex();
        File.Delete(Emails().Single() + ".meta.json");

        await using var reopened = await OpenAsync();
        var outcome = await StoreAsync(reopened, content, "abc@example.com");

        await Assert.That(outcome is Duplicate).IsTrue();
        await Assert.That(Emails().Length).IsEqualTo(1);
        await Assert.That(File.Exists(Emails().Single() + ".meta.json")).IsTrue();
        await Assert.That(await reopened.ContainsAsync("abc@example.com", CancellationToken.None)).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "tmp")).Length).IsEqualTo(0);
    }

    [Test]
    public async Task StoreAsync_NameCollisionWithDifferentContent_KeepsBothFiles()
    {
        var original = Eml.Create("abc@example.com", body: "Original");
        await using (var archive = await OpenAsync())
        {
            await StoreAsync(archive, original, "abc@example.com");
        }

        DeleteIndex();

        await using var reopened = await OpenAsync();
        var outcome = await StoreAsync(reopened, Eml.Create("abc@example.com", body: "Changed"), "abc@example.com");

        var prefix = $"{InternalDate.ToUnixTimeSeconds()}.abc@example.com";
        await Assert.That(outcome is Stored).IsTrue();
        await Assert.That(Emails().Length).IsEqualTo(2);
        await Assert.That(File.Exists(Path.Combine(Current, prefix + "_1.testhost.eml"))).IsTrue();
        await Assert.That(Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(Current, prefix + ".testhost.eml"))))
            .IsEqualTo(Convert.ToBase64String(original));
    }

    [Test]
    public async Task StoreAsync_OversizedHeader_StillArchivesMessage()
    {
        var content = Encoding.ASCII.GetBytes(
            $"X-Padding: {new string('a', 2 * 1024 * 1024)}\r\nSubject: Too big\r\n\r\nBody\r\n");
        await using var archive = await OpenAsync();

        var outcome = await StoreAsync(archive, content, "big@example.com");

        var sidecar = await File.ReadAllTextAsync(Emails().Single() + ".meta.json");
        await Assert.That(outcome is Stored).IsTrue();
        await Assert.That(new FileInfo(Emails().Single()).Length).IsEqualTo((long)content.Length);
        await Assert.That(sidecar).Contains("\"MessageId\": \"big@example.com\"");
        await Assert.That(sidecar).Contains("\"Subject\": null");
    }

    [Test]
    public async Task StoreAsync_FailedRead_LeavesNoTemporaryFile()
    {
        await using var archive = await OpenAsync();

        var act = async () => await archive.StoreAsync(
            new FailingStream(), "abc@example.com", InternalDate, "INBOX", CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(act);
        await Assert.That(Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "tmp")).Length).IsEqualTo(0);
        await Assert.That(Emails().Length).IsEqualTo(0);
        await Assert.That(await archive.ContainsAsync("abc@example.com", CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task ContainsAsync_NormalizesMessageIds()
    {
        await using var archive = await OpenAsync();
        await StoreAsync(archive, Eml.Create("abc@example.com"), "ABC@Example.com");

        await Assert.That(await archive.ContainsAsync("abc@EXAMPLE.com", CancellationToken.None)).IsTrue();
        await Assert.That(await archive.ContainsAsync("other@example.com", CancellationToken.None)).IsFalse();
    }

    private sealed class FailingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Connection reset");

        public override int Read(Span<byte> buffer) => throw new IOException("Connection reset");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Connection reset"));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(new IOException("Connection reset"));

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Connection reset"));
    }
}
