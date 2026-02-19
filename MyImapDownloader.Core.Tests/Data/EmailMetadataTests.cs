using MyImapDownloader.Core.Data;

namespace MyImapDownloader.Core.Tests.Data;

/// <summary>
/// Tests for the shared EmailMetadata record.
/// </summary>
public class EmailMetadataTests
{
    [Test]
    public async Task EmailMetadata_CanBeCreated_WithRequiredFields()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.MessageId).IsEqualTo("test@example.com");
    }

    [Test]
    public async Task EmailMetadata_OptionalFields_DefaultToNull()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.Subject).IsNull();
        await Assert.That(metadata.From).IsNull();
        await Assert.That(metadata.To).IsNull();
        await Assert.That(metadata.Cc).IsNull();
        await Assert.That(metadata.Date).IsNull();
        await Assert.That(metadata.Folder).IsNull();
        await Assert.That(metadata.SizeBytes).IsNull();
        await Assert.That(metadata.Account).IsNull();
    }

    [Test]
    public async Task EmailMetadata_HasAttachments_DefaultsFalse()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.HasAttachments).IsFalse();
    }

    [Test]
    public async Task EmailMetadata_AllFields_RoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = new EmailMetadata
        {
            MessageId = "full@example.com",
            Subject = "Test Subject",
            From = "sender@example.com",
            To = "recipient@example.com",
            Cc = "cc@example.com",
            Date = now,
            Folder = "INBOX",
            ArchivedAt = now,
            HasAttachments = true,
            SizeBytes = 1024,
            Account = "work"
        };

        await Assert.That(metadata.Subject).IsEqualTo("Test Subject");
        await Assert.That(metadata.From).IsEqualTo("sender@example.com");
        await Assert.That(metadata.HasAttachments).IsTrue();
        await Assert.That(metadata.SizeBytes).IsEqualTo(1024);
    }

    [Test]
    public async Task EmailMetadata_IsRecord_SupportsEquality()
    {
        var a = new EmailMetadata { MessageId = "same@example.com", Subject = "Same" };
        var b = new EmailMetadata { MessageId = "same@example.com", Subject = "Same" };
        var c = new EmailMetadata { MessageId = "different@example.com", Subject = "Same" };

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(c);
    }
}
