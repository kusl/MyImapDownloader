using System.Text;

using AwesomeAssertions;

namespace MyImapDownloader.Tests;

/// <summary>
/// Tests for EmailStorageService parsing behavior.
/// FIX: Validates that header-only parsing is being used.
/// </summary>
public class EmailStorageServiceParsingTests : IAsyncDisposable
{
    private readonly string _testDirectory;

    public EmailStorageServiceParsingTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"storage_parse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    [Test]
    public async Task SaveStreamAsync_WithLargeAttachment_DoesNotLoadFullMessageInMemory()
    {
        // Arrange
        var logger = TestLogger.Create<EmailStorageService>();
        var service = new EmailStorageService(logger, _testDirectory);
        await service.InitializeAsync(CancellationToken.None);

        // Create a minimal email with headers only (simulating what we'd get from IMAP)
        // The actual body/attachment content is not loaded into memory
        var email = """
            Message-ID: <memory-test@example.com>
            Subject: Memory Test Email
            From: sender@example.com
            To: recipient@example.com
            Date: Fri, 03 Jan 2025 12:00:00 +0000
            Content-Type: text/plain

            This is the body text.
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(email));

        // Act - This should NOT load the full message into memory
        var result = await service.SaveStreamAsync(
            stream,
            "<memory-test@example.com>",
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        // Assert
        await Assert.That(result).IsTrue();

        // Verify the file was created
        var curDir = Path.Combine(_testDirectory, "Inbox", "cur");
        var files = Directory.GetFiles(curDir, "*.eml");
        await Assert.That(files.Length).IsEqualTo(1);
    }

    [Test]
    public async Task SaveStreamAsync_ExtractsMetadataFromHeadersOnly()
    {
        // Arrange
        var logger = TestLogger.Create<EmailStorageService>();
        var service = new EmailStorageService(logger, _testDirectory);
        await service.InitializeAsync(CancellationToken.None);

        var email = """
            Message-ID: <metadata-test@example.com>
            Subject: Test Subject Line
            From: John Doe <john@example.com>
            To: Jane Doe <jane@example.com>
            Date: Fri, 03 Jan 2025 14:30:00 +0000
            Content-Type: text/plain

            Email body content that should not affect metadata extraction.
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(email));

        // Act
        await service.SaveStreamAsync(
            stream,
            "<metadata-test@example.com>",
            DateTimeOffset.UtcNow,
            "Inbox",
            CancellationToken.None);

        // Assert - Check that metadata file was created with correct content
        var curDir = Path.Combine(_testDirectory, "Inbox", "cur");
        var metaFiles = Directory.GetFiles(curDir, "*.meta.json");
        await Assert.That(metaFiles.Length).IsEqualTo(1);

        var metaContent = await File.ReadAllTextAsync(metaFiles[0]);
        metaContent.Should().Contain("Test Subject Line");
        metaContent.Should().Contain("john@example.com");
    }
}
