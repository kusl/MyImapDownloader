using MyImapDownloader.Core.Paths;

namespace MyImapDownloader.Core.Telemetry;

public static class TelemetryDirectories
{
    private const string Leaf = "telemetry";

    public static IEnumerable<string> Candidates(string appName, XdgDirectories xdg)
    {
        var name = appName.ToLowerInvariant();

        if (xdg.StateHomeOverride is { } stateHome)
        {
            yield return Path.Combine(stateHome, name, Leaf);
        }

        if (xdg.DataHomeOverride is { } dataHome)
        {
            yield return Path.Combine(dataHome, name, Leaf);
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName, Leaf);
        }
        else
        {
            yield return Path.Combine(xdg.Home, ".local", "state", name, Leaf);
            yield return Path.Combine(xdg.Home, ".local", "share", name, Leaf);
        }

        yield return Path.Combine(Environment.CurrentDirectory, Leaf);
    }

    public static string? FirstWritable(IEnumerable<string> candidates) => candidates.FirstOrDefault(IsWritable);

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
