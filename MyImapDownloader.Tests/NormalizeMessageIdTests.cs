using AwesomeAssertions;

namespace MyImapDownloader.Tests;

/// <summary>
/// Tests for EmailStorageService.NormalizeMessageId edge cases,
/// particularly the hash truncation path for long IDs.
/// </summary>
public class NormalizeMessageIdTests
{
    [Test]
    public async Task NormalizeMessageId_LongId_TruncatesWithHash()
    {
        // Create a message ID longer than 100 characters
        var longId = "<" + new string('a', 120) + "@example.com>";
        var normalized = EmailStorageService.NormalizeMessageId(longId);

        await Assert.That(normalized.Length).IsLessThanOrEqualTo(100);
        // Should contain a hash suffix
        normalized.Should().Contain("_");
    }

    [Test]
    public async Task NormalizeMessageId_EmptyString_ReturnsUnknown()
    {
        var normalized = EmailStorageService.NormalizeMessageId("");

        await Assert.That(normalized).IsEqualTo("unknown");
    }

    [Test]
    public async Task NormalizeMessageId_AngleBrackets_Removed()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<simple@test.com>");

        normalized.Should().NotContain("<");
        normalized.Should().NotContain(">");
    }

    [Test]
    public async Task NormalizeMessageId_SlashesReplaced()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<org/repo/id@github.com>");

        normalized.Should().NotContain("/");
        normalized.Should().NotContain("\\");
    }

    [Test]
    public async Task NormalizeMessageId_ColonsReplaced()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<urn:uuid:abc@test.com>");

        normalized.Should().NotContain(":");
    }

    [Test]
    public async Task NormalizeMessageId_ConsistentResults()
    {
        var id = "<test@example.com>";
        var first = EmailStorageService.NormalizeMessageId(id);
        var second = EmailStorageService.NormalizeMessageId(id);

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task NormalizeMessageId_CaseInsensitive()
    {
        var lower = EmailStorageService.NormalizeMessageId("<TEST@EXAMPLE.COM>");

        lower.Should().Be(lower.ToLowerInvariant());
    }

    [Test]
    public async Task SanitizeForFilename_SpecialCharsRemoved()
    {
        var result = EmailStorageService.SanitizeForFilename("hello world! @#$%", 50);

        result.Should().NotContain("!");
        result.Should().NotContain("@");
        result.Should().NotContain("#");
    }

    [Test]
    public async Task SanitizeForFilename_RespectsMaxLength()
    {
        var input = new string('a', 200);
        var result = EmailStorageService.SanitizeForFilename(input, 50);

        await Assert.That(result.Length).IsLessThanOrEqualTo(50);
    }

    [Test]
    public async Task ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = EmailStorageService.ComputeHash("input1");
        var hash2 = EmailStorageService.ComputeHash("input2");

        hash1.Should().NotBe(hash2);
    }

    [Test]
    public async Task ComputeHash_ReturnsLowercaseHex()
    {
        var hash = EmailStorageService.ComputeHash("test");

        hash.Should().MatchRegex("^[0-9a-f]+$");
    }
}
