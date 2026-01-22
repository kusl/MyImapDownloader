using System;
using System.IO;
using System.Threading.Tasks;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MyImapDownloader.Tests;

public class EmailDownloadExceptionTests
{
    [Test]
    public async Task Constructor_SetsMessage()
    {
        var ex = new EmailDownloadException(
            "Test error",
            42,
            new InvalidOperationException("Inner"));

        await Assert.That(ex.Message).IsEqualTo("Test error");
    }

    [Test]
    public async Task Constructor_SetsMessageIndex()
    {
        var ex = new EmailDownloadException(
            "Test error",
            42,
            new InvalidOperationException("Inner"));

        await Assert.That(ex.MessageIndex).IsEqualTo(42);
    }

    [Test]
    public async Task Constructor_SetsInnerException()
    {
        var inner = new InvalidOperationException("Inner error");
        var ex = new EmailDownloadException("Test", 0, inner);

        await Assert.That(ex.InnerException).IsEqualTo(inner);
    }

    [Test]
    public async Task Exception_CanBeThrown()
    {
        var act = () =>
        {
            throw new EmailDownloadException(
                "Download failed",
                5,
                new IOException("Network error"));
        };

        await Assert.That(act).ThrowsException();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(100)]
    [Arguments(int.MaxValue)]
    public async Task MessageIndex_AcceptsVariousValues(int index)
    {
        var ex = new EmailDownloadException(
            "Test",
            index,
            new Exception());

        await Assert.That(ex.MessageIndex).IsEqualTo(index);
    }
}
