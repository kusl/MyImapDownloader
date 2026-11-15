using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Indexing;

namespace MyEmailSearch.Tests;

public class EmailParserTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly EmailParser _parser = new(NullLogger<EmailParser>.Instance, TimeProvider.System);

    public void Dispose() => _temp.Dispose();

    private string Write(string name, string content)
    {
        var directory = Path.Combine(_temp.Path, "work", "INBOX", "cur");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task ParseAsync_ExtractsHeadersAndLocation()
    {
        var path = Write("one.eml", Sample.Email("one@example.com", "Quarterly report", "The   kafka\r\ncluster is healthy."));

        var document = await _parser.ParseAsync(path, _temp.Path, includeFullBody: true);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.MessageId).IsEqualTo("one@example.com");
        await Assert.That(document.FilePath).IsEqualTo(path);
        await Assert.That(document.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(document.FromName).IsEqualTo("Alice");
        await Assert.That(document.ToAddresses.Count).IsEqualTo(2);
        await Assert.That(document.ToAddresses[0]).IsEqualTo("bob@example.com");
        await Assert.That(document.ToAddresses[1]).IsEqualTo("carol@example.com");
        await Assert.That(document.CcAddresses[0]).IsEqualTo("dave@example.com");
        await Assert.That(document.Subject).IsEqualTo("Quarterly report");
        await Assert.That(document.DateSentUnix).IsEqualTo(1735036200L);
        await Assert.That(document.Account).IsEqualTo("work");
        await Assert.That(document.Folder).IsEqualTo("INBOX");
        await Assert.That(document.HasAttachments).IsFalse();
        await Assert.That(document.BodyText).IsEqualTo("The kafka cluster is healthy.");
        await Assert.That(document.BodyPreview).IsEqualTo("The kafka cluster is healthy.");
        await Assert.That(document.LastModifiedTicks).IsEqualTo(File.GetLastWriteTimeUtc(path).Ticks);
    }

    [Test]
    public async Task ParseAsync_OmitsFullBodyUnlessRequestedAndTruncatesPreview()
    {
        var path = Write("long.eml", Sample.Email("long@example.com", "Long", new string('a', 600)));

        var document = await _parser.ParseAsync(path, _temp.Path, includeFullBody: false);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.BodyText).IsNull();
        await Assert.That(document.BodyPreview).IsEqualTo(new string('a', 500) + "...");
    }

    [Test]
    public async Task ParseAsync_WithoutMessageIdFallsBackToFileName()
    {
        var path = Write("1702900000.fallback.host.eml", Sample.Email(null, "No id", "Body"));

        var document = await _parser.ParseAsync(path, _temp.Path, includeFullBody: false);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.MessageId).IsEqualTo("1702900000.fallback.host");
    }

    [Test]
    public async Task ParseAsync_HtmlOnlyMessageIsReducedToText()
    {
        var path = Write("html.eml",
            "Message-ID: <html@example.com>\r\nFrom: alice@example.com\r\nSubject: Html\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n" +
            "<html><head><style>p { color: red; }</style></head><body><p>Fish &amp; chips</p><script>alert(1)</script></body></html>\r\n");

        var document = await _parser.ParseAsync(path, _temp.Path, includeFullBody: true);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.BodyText).IsEqualTo("Fish & chips");
    }

    [Test]
    public async Task ParseAsync_ListsAttachmentNames()
    {
        var path = Write("attachment.eml",
            "Message-ID: <att@example.com>\r\nFrom: alice@example.com\r\nSubject: Files\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"sep\"\r\n\r\n" +
            "--sep\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee attached.\r\n" +
            "--sep\r\nContent-Type: application/pdf; name=\"report.pdf\"\r\n" +
            "Content-Disposition: attachment; filename=\"report.pdf\"\r\nContent-Transfer-Encoding: base64\r\n\r\nJVBERg==\r\n" +
            "--sep--\r\n");

        var document = await _parser.ParseAsync(path, _temp.Path, includeFullBody: true);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.HasAttachments).IsTrue();
        await Assert.That(document.AttachmentNames.Count).IsEqualTo(1);
        await Assert.That(document.AttachmentNames[0]).IsEqualTo("report.pdf");
        await Assert.That(document.BodyText).IsEqualTo("See attached.");
    }

    [Test]
    public async Task ParseAsync_MissingFileReturnsNull()
    {
        var document = await _parser.ParseAsync(Path.Combine(_temp.Path, "missing.eml"), _temp.Path, includeFullBody: false);

        await Assert.That(document).IsNull();
    }

    [Test]
    public async Task HtmlToText_DropsMarkupAndDecodesEntities()
    {
        var text = EmailParser.HtmlToText("<div>Tom &amp; Jerry&nbsp;&lt;3</div><STYLE>x{}</STYLE>");

        await Assert.That(text.Replace(' ', ' ').Trim()).IsEqualTo("Tom & Jerry <3");
    }
}
