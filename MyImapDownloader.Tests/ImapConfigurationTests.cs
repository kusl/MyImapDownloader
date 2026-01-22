namespace MyImapDownloader.Tests;

public class ImapConfigurationTests
{
    [Test]
    public async Task RequiredProperties_MustBeSet()
    {
        var config = new ImapConfiguration
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret"
        };

        await Assert.That(config.Server).IsEqualTo("imap.example.com");
        await Assert.That(config.Username).IsEqualTo("user@example.com");
        await Assert.That(config.Password).IsEqualTo("secret");
    }

    [Test]
    public async Task UseSsl_DefaultsToTrue()
    {
        var config = new ImapConfiguration
        {
            Server = "test",
            Username = "test",
            Password = "test"
        };

        await Assert.That(config.UseSsl).IsTrue();
    }

    [Test]
    public async Task Port_CanBeSet()
    {
        var config = new ImapConfiguration
        {
            Server = "test",
            Username = "test",
            Password = "test",
            Port = 143
        };

        await Assert.That(config.Port).IsEqualTo(143);
    }

    [Test]
    [Arguments(993, true)]
    [Arguments(143, false)]
    [Arguments(587, false)]
    public async Task CommonConfigurations_AreValid(int port, bool useSsl)
    {
        var config = new ImapConfiguration
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret",
            Port = port,
            UseSsl = useSsl
        };

        await Assert.That(config.Port).IsEqualTo(port);
        await Assert.That(config.UseSsl).IsEqualTo(useSsl);
    }
}
