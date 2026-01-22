namespace MyImapDownloader.Tests;

public class DownloadOptionsTests
{
    [Test]
    public async Task RequiredProperties_MustBeSet()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret",
            OutputDirectory = "/output"
        };

        await Assert.That(options.Server).IsEqualTo("imap.example.com");
        await Assert.That(options.Username).IsEqualTo("user@example.com");
        await Assert.That(options.Password).IsEqualTo("secret");
        await Assert.That(options.OutputDirectory).IsEqualTo("/output");
    }

    [Test]
    public async Task Port_DefaultsToZero()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.Port).IsEqualTo(993);
    }

    [Test]
    public async Task AllFolders_DefaultsToFalse()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.AllFolders).IsFalse();
    }

    [Test]
    public async Task Verbose_DefaultsToFalse()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.Verbose).IsFalse();
    }

    [Test]
    public async Task StartDate_CanBeSet()
    {
        var date = new DateTime(2024, 1, 1);
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test",
            StartDate = date
        };

        await Assert.That(options.StartDate).IsEqualTo(date);
    }

    [Test]
    public async Task EndDate_CanBeSet()
    {
        var date = new DateTime(2024, 12, 31);
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test",
            EndDate = date
        };

        await Assert.That(options.EndDate).IsEqualTo(date);
    }
}
