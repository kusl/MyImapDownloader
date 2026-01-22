using AwesomeAssertions;

namespace MyImapDownloader.Tests.Services;

public class EmailStorageSanitizationTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("sanitize_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    [Arguments("<simple@test.com>", "simple_test.com")]
    [Arguments("<path/with/slashes@test.com>", "path_with_slashes_test.com")]
    [Arguments("<spaces here@test.com>", "spaces_here_test.com")]
    public async Task NormalizeMessageId_SanitizesCorrectly(string input, string expected)
    {
        var result = EmailStorageService.NormalizeMessageId(input);
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
    }

    [Test]
    public async Task SanitizeForFilename_TruncatesLongInput()
    {
        var longInput = new string('a', 200);
        var result = EmailStorageService.SanitizeForFilename(longInput, 50);

        await Assert.That(result.Length).IsLessThanOrEqualTo(50);
    }

    [Test]
    public async Task SanitizeForFilename_RemovesInvalidChars()
    {
        var input = "test<>:\"/\\|?*file";
        var result = EmailStorageService.SanitizeForFilename(input, 100);

        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().NotContain(":");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
    }

    [Test]
    public async Task GenerateFilename_IsValidFilename()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var filename = EmailStorageService.GenerateFilename(date, "test_id");

        await Assert.That(Path.GetFileName(filename)).IsEqualTo(filename);
        filename.Should().EndWith(".eml");
    }
}
