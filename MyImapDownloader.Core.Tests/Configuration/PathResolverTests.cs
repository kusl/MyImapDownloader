using MyImapDownloader.Core.Configuration;

namespace MyImapDownloader.Core.Tests.Configuration;

public class PathResolverTests
{
    [Test]
    public async Task GetDataHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDataHome("TestApp");

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetConfigHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetConfigHome("TestApp");

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetStateHome_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetStateHome("TestApp");

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task EnsureWritableDirectory_CreatesDirectory()
    {
        using var temp = new MyImapDownloader.Core.Infrastructure.TempDirectory("path_test");
        var subDir = Path.Combine(temp.Path, "subdir");

        var result = PathResolver.EnsureWritableDirectory(subDir);

        await Assert.That(result).IsTrue();
        await Assert.That(Directory.Exists(subDir)).IsTrue();
    }

    [Test]
    public async Task FindFirstExisting_ReturnsFirstMatch()
    {
        using var temp = new MyImapDownloader.Core.Infrastructure.TempDirectory("find_test");

        var result = PathResolver.FindFirstExisting(
            "/nonexistent/path",
            temp.Path,
            "/another/nonexistent");

        await Assert.That(result).IsEqualTo(temp.Path);
    }
}
