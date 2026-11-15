using MyImapDownloader.Core.Paths;

namespace MyEmailSearch.Configuration;

public static class SearchPaths
{
    public const string ArchiveVariable = "MYIMAPDOWNLOADER_ARCHIVE";

    private const string DownloaderDirectory = "myimapdownloader";
    private const string SearchDirectory = "myemailsearch";

    public static string DefaultArchive() => DefaultArchive(
        XdgDirectories.Current,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Directory.Exists);

    public static string DefaultArchive(XdgDirectories xdg, string documents, Func<string, bool> exists)
    {
        IEnumerable<string?> candidates =
        [
            xdg.Variable(ArchiveVariable),
            xdg.DataHomeOverride is { } dataHome ? Path.Combine(dataHome, DownloaderDirectory) : null,
            Path.Combine(xdg.Home, ".local", "share", DownloaderDirectory),
            Path.Combine(xdg.Home, "Documents", "mail"),
            Path.Combine(xdg.Home, "mail"),
            Path.Combine(documents, "mail")
        ];

        return candidates.OfType<string>().FirstOrDefault(exists)
            ?? Path.Combine(xdg.DataHome, DownloaderDirectory);
    }

    public static string DefaultDatabase() => DefaultDatabase(XdgDirectories.Current);

    public static string DefaultDatabase(XdgDirectories xdg) =>
        Path.Combine(xdg.DataHome, SearchDirectory, "search.db");
}
