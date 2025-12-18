using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace MyImapDownloader.Tests;

public class ImapConfigurationTests
{
    [Test]
    public async Task DefaultValues_AreSet()
    {
        var config = new ImapConfiguration();

        await Assert.That(config.Port).IsEqualTo(993);
        await Assert.That(config.UseSsl).IsTrue();
        await Assert.That(config.Server).IsEmpty();
        await Assert.That(config.Username).IsEmpty();
        await Assert.That(config.Password).IsEmpty();
    }

    [Test]
    public async Task SectionName_IsCorrect()
    {
        await Assert.That(ImapConfiguration.SectionName).IsEqualTo("Imap");
    }

    [Test]
    public async Task CanBindFromConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Imap:Server"] = "imap.example.com",
            ["Imap:Port"] = "587",
            ["Imap:Username"] = "user@example.com",
            ["Imap:Password"] = "secret123",
            ["Imap:UseSsl"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var imapConfig = new ImapConfiguration();
        configuration.GetSection("Imap").Bind(imapConfig);

        await Assert.That(imapConfig.Server).IsEqualTo("imap.example.com");
        await Assert.That(imapConfig.Port).IsEqualTo(587);
        await Assert.That(imapConfig.Username).IsEqualTo("user@example.com");
        await Assert.That(imapConfig.Password).IsEqualTo("secret123");
        await Assert.That(imapConfig.UseSsl).IsFalse();
    }

    [Test]
    public async Task PartialConfiguration_UsesDefaults()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Imap:Server"] = "mail.test.com",
            ["Imap:Username"] = "testuser"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var imapConfig = new ImapConfiguration();
        configuration.GetSection("Imap").Bind(imapConfig);

        // Configured values
        await Assert.That(imapConfig.Server).IsEqualTo("mail.test.com");
        await Assert.That(imapConfig.Username).IsEqualTo("testuser");
        
        // Default values preserved
        await Assert.That(imapConfig.Port).IsEqualTo(993);
        await Assert.That(imapConfig.UseSsl).IsTrue();
    }

    [Test]
    [Arguments(993, true)]   // Standard IMAPS
    [Arguments(143, false)]  // Standard IMAP
    [Arguments(587, true)]   // Custom with SSL
    [Arguments(2993, true)]  // Non-standard with SSL
    public async Task PortAndSslCombinations_AreValid(int port, bool useSsl)
    {
        var config = new ImapConfiguration
        {
            Port = port,
            UseSsl = useSsl
        };

        await Assert.That(config.Port).IsEqualTo(port);
        await Assert.That(config.UseSsl).IsEqualTo(useSsl);
    }
}
