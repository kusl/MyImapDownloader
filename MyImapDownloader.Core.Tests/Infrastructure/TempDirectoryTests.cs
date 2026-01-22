using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Core.Tests.Infrastructure;

public class TempDirectoryTests
{
    [Test]
    public async Task Constructor_CreatesDirectory()
    {
        using var temp = new TempDirectory("test");
        await Assert.That(Directory.Exists(temp.Path)).IsTrue();
    }

    [Test]
    public async Task Dispose_DeletesDirectory()
    {
        string path;
        using (var temp = new TempDirectory("dispose_test"))
        {
            path = temp.Path;
            await Assert.That(Directory.Exists(path)).IsTrue();
        }

        await Task.Delay(100); // Give filesystem time
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    [Test]
    public async Task Path_ContainsPrefix()
    {
        using var temp = new TempDirectory("myprefix");
        await Assert.That(temp.Path).Contains("myprefix");
    }
}
