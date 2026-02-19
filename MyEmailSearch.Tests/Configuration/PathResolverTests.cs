using MyEmailSearch.Configuration;

namespace MyEmailSearch.Tests.Configuration;

/// <summary>
/// Tests for MyEmailSearch.Configuration.PathResolver.
/// </summary>
public class PathResolverTests
{
    [Test]
    public async Task GetDefaultDatabasePath_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetDefaultDatabasePath_EndsWithDbExtension()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path).EndsWith(".db");
    }

    [Test]
    public async Task GetDefaultArchivePath_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDefaultArchivePath();

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetDefaultDatabasePath_ContainsMyEmailSearch()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path.ToLowerInvariant()).Contains("myemailsearch");
    }
}
