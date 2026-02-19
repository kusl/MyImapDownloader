using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Indexing;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for EmailParser edge cases: multipart, HTML-only, attachments, malformed.
/// </summary>
public class EmailParserEdgeCaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("parser_edge_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task<string> CreateEmlFileAsync(string content)
    {
        var path = Path.Combine(_temp.Path, $"{Guid.NewGuid()}.eml");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Test]
    public async Task ParseAsync_MultipartAlternative_ExtractsPlainText()
    {
        var emlContent = "Message-ID: <multi@example.com>\r\n" +
            "Subject: Multipart Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/alternative; boundary=\"boundary123\"\r\n" +
            "\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "This is the plain text version.\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><p>This is HTML</p></body></html>\r\n" +
            "--boundary123--\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc.Should().NotBeNull();
        doc!.BodyText.Should().Contain("plain text version");
    }

    [Test]
    public async Task ParseAsync_HtmlOnlyEmail_ExtractsText()
    {
        var emlContent = "Message-ID: <html@example.com>\r\n" +
            "Subject: HTML Only\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><p>HTML only content here</p></body></html>\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc.Should().NotBeNull();
        // Should get something from the HTML even if it's the raw HTML
        doc!.BodyText.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ParseAsync_EmailWithAttachment_SetsHasAttachments()
    {
        var emlContent = "Message-ID: <attach@example.com>\r\n" +
            "Subject: Attachment Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"mixedboundary\"\r\n" +
            "\r\n" +
            "--mixedboundary\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "See attached.\r\n" +
            "--mixedboundary\r\n" +
            "Content-Type: application/pdf; name=\"report.pdf\"\r\n" +
            "Content-Disposition: attachment; filename=\"report.pdf\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "\r\n" +
            "JVBERi0xLjQKMSAwIG9iago=\r\n" +
            "--mixedboundary--\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        await Assert.That(doc!.HasAttachments).IsTrue();
        doc.AttachmentNamesJson.Should().Contain("report.pdf");
    }

    [Test]
    public async Task ParseAsync_MissingMessageId_StillParses()
    {
        var emlContent = "Subject: No Message ID\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body content\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        // Should still parse, possibly with null or generated message ID
        doc.Should().NotBeNull();
    }

    [Test]
    public async Task ParseAsync_EmptyFile_ReturnsNull()
    {
        var path = await CreateEmlFileAsync("");
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc).IsNull();
    }

    [Test]
    public async Task ParseAsync_MultipleRecipients_ExtractsAll()
    {
        var emlContent = "Message-ID: <multi-to@example.com>\r\n" +
            "Subject: Multiple Recipients\r\n" +
            "From: sender@example.com\r\n" +
            "To: alice@example.com, bob@example.com\r\n" +
            "Cc: charlie@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Group email\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        doc!.ToAddressesJson.Should().Contain("alice@example.com");
        doc.ToAddressesJson.Should().Contain("bob@example.com");
        doc.CcAddressesJson.Should().Contain("charlie@example.com");
    }

    [Test]
    public async Task ParseAsync_IncludeFullBody_False_LimitsBodyLength()
    {
        var longBody = new string('A', 2000);
        var emlContent = "Message-ID: <preview@example.com>\r\n" +
            "Subject: Long Body\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            longBody + "\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        // Body preview should be truncated (500 chars per BodyPreviewLength constant)
        doc!.BodyPreview.Should().NotBeNull();
        await Assert.That(doc.BodyPreview!.Length).IsLessThanOrEqualTo(510);
    }

    [Test]
    public async Task ParseAsync_SetsLastModifiedTicks()
    {
        var emlContent = "Message-ID: <ticks@example.com>\r\n" +
            "Subject: Ticks Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.LastModifiedTicks).IsGreaterThan(0);
    }
}
