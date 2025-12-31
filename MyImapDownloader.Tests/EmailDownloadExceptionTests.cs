using AwesomeAssertions;

namespace MyImapDownloader.Tests;

public class EmailDownloadExceptionTests
{
    [Test]
    public async Task Constructor_SetsAllProperties()
    {
        var inner = new IOException("Network error");
        var exception = new EmailDownloadException("Download failed", 42, inner);

        await Assert.That(exception.Message).IsEqualTo("Download failed");
        await Assert.That(exception.MessageIndex).IsEqualTo(42);
        await Assert.That(exception.InnerException).IsEqualTo(inner);
    }

    [Test]
    public async Task MessageIndex_IsAccessible()
    {
        var inner = new Exception("Inner");
        var exception = new EmailDownloadException("Error at index", 5, inner);

        await Assert.That(exception.MessageIndex).IsEqualTo(5);
    }

    [Test]
    public async Task InheritsFromException()
    {
        var inner = new Exception("Inner");
        var exception = new EmailDownloadException("Test", 0, inner);

        exception.Should().BeAssignableTo<Exception>();
    }

    [Test]
    public async Task CanBeCaughtAsException()
    {
        Exception? caught = null;
        var inner = new Exception("Inner");

        try
        {
            throw new EmailDownloadException("Test error", 1, inner);
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
        var inner = new Exception("Inner");

        try
        {
            throw new EmailDownloadException("Specific error", 10, inner);
        }
        catch (EmailDownloadException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo("Specific error");
        await Assert.That(caught.MessageIndex).IsEqualTo(10);
    }

    [Test]
    public async Task InnerException_ChainIsPreserved()
    {
        var level1 = new InvalidOperationException("Level 1");
        var level2 = new EmailDownloadException("Level 2", 1, level1);
        var level3 = new EmailDownloadException("Level 3", 2, level2);

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

    [Test]
    public async Task MessageIndex_ZeroIsValid()
    {
        var inner = new Exception("Inner");
        var exception = new EmailDownloadException("First message failed", 0, inner);

        await Assert.That(exception.MessageIndex).IsEqualTo(0);
    }

    [Test]
    public async Task MessageIndex_LargeValueIsValid()
    {
        var inner = new Exception("Inner");
        var exception = new EmailDownloadException("Message failed", 999999, inner);

        await Assert.That(exception.MessageIndex).IsEqualTo(999999);
    }

    private static void ThrowHelper()
    {
        var inner = new Exception("Inner");
        throw new EmailDownloadException("From helper", 0, inner);
    }
}
