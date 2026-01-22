using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class EmailParserTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("parser_test");

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
    public async Task ParseAsync_ExtractsMessageId()
    {
        var emlContent = """
            Message-ID: <test123@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Hello world
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        await Assert.That(doc!.MessageId).IsEqualTo("test123@example.com");
    }

    [Test]
    public async Task ParseAsync_ExtractsSubject()
    {
        var emlContent = """
            Message-ID: <subject@example.com>
            Subject: Important Meeting Tomorrow
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.Subject).IsEqualTo("Important Meeting Tomorrow");
    }

    [Test]
    public async Task ParseAsync_ExtractsFromAddress()
    {
        var emlContent = """
            Message-ID: <from@example.com>
            Subject: Test
            From: Alice Smith <alice@example.com>
            To: bob@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc!.FromAddress.Should().Contain("alice@example.com");
    }

    [Test]
    public async Task ParseAsync_ExtractsBodyText_WhenRequested()
    {
        var emlContent = """
            Message-ID: <body@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            This is the email body content.
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc!.BodyText.Should().Contain("email body content");
    }

    [Test]
    public async Task ParseAsync_SetsIndexedAtUnix()
    {
        var emlContent = """
            Message-ID: <indexed@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.IndexedAtUnix).IsGreaterThan(0);
    }

    [Test]
    public async Task ParseAsync_ReturnsNullForInvalidFile()
    {
        var path = Path.Combine(_temp.Path, "nonexistent.eml");
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc).IsNull();
    }
}
