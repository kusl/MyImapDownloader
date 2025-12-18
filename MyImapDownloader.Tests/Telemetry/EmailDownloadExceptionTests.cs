using FluentAssertions;

namespace MyImapDownloader.Tests;

public class EmailDownloadExceptionTests
{
    [Test]
    public async Task Constructor_WithMessage_SetsMessage()
    {
        var exception = new EmailDownloadException("Download failed");

        await Assert.That(exception.Message).IsEqualTo("Download failed");
    }

    [Test]
    public async Task Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var inner = new IOException("Network error");
        var exception = new EmailDownloadException("Download failed", inner);

        await Assert.That(exception.Message).IsEqualTo("Download failed");
        await Assert.That(exception.InnerException).IsEqualTo(inner);
    }

    [Test]
    public async Task InheritsFromException()
    {
        var exception = new EmailDownloadException("Test");

        exception.Should().BeAssignableTo<Exception>();
    }

    [Test]
    public async Task CanBeCaughtAsException()
    {
        Exception? caught = null;

        try
        {
            throw new EmailDownloadException("Test error");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        caught.Should().BeOfType<EmailDownloadException>();
    }

    [Test]
    public async Task CanBeCaughtSpecifically()
    {
        EmailDownloadException? caught = null;

        try
        {
            throw new EmailDownloadException("Specific error");
        }
        catch (EmailDownloadException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo("Specific error");
    }

    [Test]
    public async Task InnerException_ChainIsPreserved()
    {
        var level1 = new InvalidOperationException("Level 1");
        var level2 = new EmailDownloadException("Level 2", level1);
        var level3 = new EmailDownloadException("Level 3", level2);

        await Assert.That(level3.InnerException).IsEqualTo(level2);
        await Assert.That(level3.InnerException!.InnerException).IsEqualTo(level1);
    }

    [Test]
    public async Task StackTrace_IsAvailable_WhenThrown()
    {
        EmailDownloadException? caught = null;

        try
        {
            ThrowHelper();
        }
        catch (EmailDownloadException ex)
        {
            caught = ex;
        }

        caught!.StackTrace.Should().NotBeNullOrEmpty();
        caught.StackTrace.Should().Contain("ThrowHelper");
    }

    private static void ThrowHelper()
    {
        throw new EmailDownloadException("From helper");
    }
}
