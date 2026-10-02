using Microsoft.Extensions.Logging.Abstractions;

namespace MyImapDownloader.Tests;

public class EmailStorageServiceNoMessageIdTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private static MemoryStream CreateRawEmailWithoutMessageId(string body)
    {
        var raw = $"""
            From: sender@test.com
            To: receiver@test.com
            Subject: No identifier
            Date: Wed, 01 May 2024 12:00:00 +0000

            {body}
            """;

        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    [Test]
    public async Task DistinctMessagesWithSameInternalDate_AreBothSaved()
    {
        await using var service = new EmailStorageService(NullLogger<EmailStorageService>.Instance, _temp.Path);
        await service.InitializeAsync(CancellationToken.None);

        var internalDate = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero);

        using var first = CreateRawEmailWithoutMessageId("First body");
        using var second = CreateRawEmailWithoutMessageId("Second body");

        var savedFirst = await service.SaveStreamAsync(first, "", internalDate, "INBOX", CancellationToken.None);
        var savedSecond = await service.SaveStreamAsync(second, "", internalDate, "INBOX", CancellationToken.None);

        await Assert.That(savedFirst).IsTrue();
        await Assert.That(savedSecond).IsTrue();

        var emlFiles = Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "cur"), "*.eml");
        await Assert.That(emlFiles.Length).IsEqualTo(2);
    }

    [Test]
    public async Task IdenticalMessagesWithoutMessageId_AreDeduplicated()
    {
        await using var service = new EmailStorageService(NullLogger<EmailStorageService>.Instance, _temp.Path);
        await service.InitializeAsync(CancellationToken.None);

        var internalDate = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero);

        using var first = CreateRawEmailWithoutMessageId("Same body");
        using var second = CreateRawEmailWithoutMessageId("Same body");

        var savedFirst = await service.SaveStreamAsync(first, "", internalDate, "INBOX", CancellationToken.None);
        var savedSecond = await service.SaveStreamAsync(second, "", internalDate.AddMinutes(5), "Archive", CancellationToken.None);

        await Assert.That(savedFirst).IsTrue();
        await Assert.That(savedSecond).IsFalse();
    }
}
