namespace MyEmailSearch.Indexing;

public static class ArchiveScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive
    };

    public static IEnumerable<string> FindEmails(string archivePath) =>
        Directory.Exists(archivePath) ? Directory.EnumerateFiles(archivePath, "*.eml", Options) : [];

    public static string? AccountOf(string filePath, string archivePath) =>
        Segments(filePath, archivePath) is { Length: >= 2 } parts ? parts[0] : null;

    public static string? FolderOf(string filePath, string archivePath) =>
        Segments(filePath, archivePath) is { Length: >= 3 } parts ? parts[1] : null;

    private static string[] Segments(string filePath, string archivePath) =>
        Path.GetRelativePath(archivePath, filePath).Split(Path.DirectorySeparatorChar);
}
