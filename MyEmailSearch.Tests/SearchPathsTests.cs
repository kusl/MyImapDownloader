using MyEmailSearch.Configuration;

using MyImapDownloader.Core.Paths;

namespace MyEmailSearch.Tests;

public class SearchPathsTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "home");
    private static readonly string Documents = Path.Combine(Home, "MyDocuments");

    private static XdgDirectories Xdg(string? archive = null, string? dataHome = null) => new(
        name => name switch
        {
            SearchPaths.ArchiveVariable => archive,
            "XDG_DATA_HOME" => dataHome,
            _ => null
        },
        Home);

    [Test]
    public async Task DefaultDatabase_LivesUnderDataHome()
    {
        await Assert.That(SearchPaths.DefaultDatabase(Xdg()))
            .IsEqualTo(Path.Combine(Home, ".local", "share", "myemailsearch", "search.db"));
        await Assert.That(SearchPaths.DefaultDatabase(Xdg(dataHome: "/data")))
            .IsEqualTo(Path.Combine("/data", "myemailsearch", "search.db"));
    }

    [Test]
    public async Task DefaultArchive_PrefersExistingEnvironmentPath()
    {
        var archive = SearchPaths.DefaultArchive(Xdg(archive: "/custom", dataHome: "/data"), Documents, _ => true);

        await Assert.That(archive).IsEqualTo("/custom");
    }

    [Test]
    public async Task DefaultArchive_SkipsCandidatesThatDoNotExist()
    {
        var documentsMail = Path.Combine(Home, "Documents", "mail");

        var archive = SearchPaths.DefaultArchive(
            Xdg(archive: "/custom", dataHome: "/data"), Documents, path => path == documentsMail);

        await Assert.That(archive).IsEqualTo(documentsMail);
    }

    [Test]
    public async Task DefaultArchive_FallsBackToDataHome()
    {
        await Assert.That(SearchPaths.DefaultArchive(Xdg(), Documents, _ => false))
            .IsEqualTo(Path.Combine(Home, ".local", "share", "myimapdownloader"));
        await Assert.That(SearchPaths.DefaultArchive(Xdg(dataHome: "/data"), Documents, _ => false))
            .IsEqualTo(Path.Combine("/data", "myimapdownloader"));
    }
}
