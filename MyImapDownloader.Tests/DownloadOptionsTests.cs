namespace MyImapDownloader.Tests;

public class DownloadOptionsTests
{
    [Test]
    public async Task DefaultValues_AreSet()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output"
        };

        await Assert.That(options.Port).IsEqualTo(993);
        await Assert.That(options.StartDate).IsNull();
        await Assert.That(options.EndDate).IsNull();
        await Assert.That(options.AllFolders).IsFalse();
        await Assert.That(options.Verbose).IsFalse();
    }

    [Test]
    public async Task RequiredProperties_MustBeSet()
    {
        var options = new DownloadOptions
        {
            Server = "mail.test.com",
            Username = "testuser",
            Password = "testpass",
            OutputDirectory = "emails"
        };

        await Assert.That(options.Server).IsEqualTo("mail.test.com");
        await Assert.That(options.Username).IsEqualTo("testuser");
        await Assert.That(options.Password).IsEqualTo("testpass");
        await Assert.That(options.OutputDirectory).IsEqualTo("emails");
    }

    [Test]
    public async Task Port_CanBeCustomized()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output",
            Port = 143
        };

        await Assert.That(options.Port).IsEqualTo(143);
    }

    [Test]
    public async Task DateFilters_CanBeSet()
    {
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output",
            StartDate = startDate,
            EndDate = endDate
        };

        await Assert.That(options.StartDate).IsEqualTo(startDate);
        await Assert.That(options.EndDate).IsEqualTo(endDate);
    }

    [Test]
    public async Task DateRange_CanBeCalculated()
    {
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output",
            StartDate = startDate,
            EndDate = endDate
        };

        var daySpan = (options.EndDate!.Value - options.StartDate!.Value).Days;
        await Assert.That(daySpan).IsEqualTo(365);
    }

    [Test]
    public async Task AllFolders_CanBeEnabled()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output",
            AllFolders = true
        };

        await Assert.That(options.AllFolders).IsTrue();
    }

    [Test]
    public async Task Verbose_CanBeEnabled()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output",
            Verbose = true
        };

        await Assert.That(options.Verbose).IsTrue();
    }

    [Test]
    public async Task NullableDateProperties_CanRemainNull()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "password123",
            OutputDirectory = "output"
        };

        await Assert.That(options.StartDate).IsNull();
        await Assert.That(options.EndDate).IsNull();
    }
}
