using FluentAssertions;

namespace MyImapDownloader.Tests;

public class DownloadOptionsTests
{
    [Test]
    public async Task DefaultValues_AreReasonable()
    {
        var options = new DownloadOptions();

        await Assert.That(options.Folder).IsEqualTo("INBOX");
        await Assert.That(options.Limit).IsNull();
        await Assert.That(options.OutputPath).IsNull();
        await Assert.That(options.Since).IsNull();
        await Assert.That(options.Before).IsNull();
        await Assert.That(options.Verbose).IsFalse();
    }

    [Test]
    public async Task AllPropertiesCanBeSet()
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var before = DateTime.UtcNow;

        var options = new DownloadOptions
        {
            Folder = "Sent",
            Limit = 100,
            OutputPath = "/output/emails",
            Since = since,
            Before = before,
            Verbose = true
        };

        await Assert.That(options.Folder).IsEqualTo("Sent");
        await Assert.That(options.Limit).IsEqualTo(100);
        await Assert.That(options.OutputPath).IsEqualTo("/output/emails");
        await Assert.That(options.Since).IsEqualTo(since);
        await Assert.That(options.Before).IsEqualTo(before);
        await Assert.That(options.Verbose).IsTrue();
    }

    [Test]
    [Arguments("INBOX")]
    [Arguments("Sent")]
    [Arguments("Drafts")]
    [Arguments("INBOX/Subfolder")]
    [Arguments("[Gmail]/All Mail")]
    [Arguments("Archive/2024")]
    public async Task Folder_AcceptsVariousFormats(string folder)
    {
        var options = new DownloadOptions { Folder = folder };
        
        await Assert.That(options.Folder).IsEqualTo(folder);
    }

    [Test]
    [Arguments(1)]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(int.MaxValue)]
    public async Task Limit_AcceptsPositiveIntegers(int limit)
    {
        var options = new DownloadOptions { Limit = limit };
        
        await Assert.That(options.Limit).IsEqualTo(limit);
    }

    [Test]
    public async Task DateRange_CanSpanMultipleDays()
    {
        var since = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var options = new DownloadOptions
        {
            Since = since,
            Before = before
        };

        var daySpan = (options.Before!.Value - options.Since!.Value).Days;
        await Assert.That(daySpan).IsEqualTo(365);
    }

    [Test]
    public async Task NullableProperties_CanRemainNull()
    {
        var options = new DownloadOptions
        {
            Folder = "Custom"
        };

        await Assert.That(options.Limit).IsNull();
        await Assert.That(options.OutputPath).IsNull();
        await Assert.That(options.Since).IsNull();
        await Assert.That(options.Before).IsNull();
    }
}
